import React, { useState, useEffect, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Send, Plus, Loader2, Bot, User, FileText, X } from 'lucide-react';
import { apiClient } from '@/api/client';
import type { ChatSession, ChatSessionDetail } from '@/types/api';
import { useLanguage } from '@/hooks/useLanguage';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { cn } from '@/lib/utils';

export function Copilot() {
  const { language } = useLanguage();
  const isEn = language === 'en';
  const queryClient = useQueryClient();
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const [activeSessionId, setActiveSessionId] = useState<string | null>(null);
  const [input, setInput] = useState('');
  const [streamedResponse, setStreamedResponse] = useState('');
  const [isStreaming, setIsStreaming] = useState(false);
  const [activeCitations, setActiveCitations] = useState<any[]>([]);
  const [selectedCitation, setSelectedCitation] = useState<any | null>(null);

  // Fetch list of sessions
  const { data: sessionsResponse } = useQuery<{ sessions: ChatSession[] }>({
    queryKey: ['sessions'],
    queryFn: async () => {
      const res = await apiClient.get('/api/sessions');
      return res.data;
    },
  });
  
  const sessions = sessionsResponse?.sessions || [];

  // Fetch active session details
  const { data: activeSession, isLoading: isLoadingSession } = useQuery<ChatSessionDetail>({
    queryKey: ['session', activeSessionId],
    queryFn: async () => {
      const res = await apiClient.get(`/api/sessions/${activeSessionId}`);
      return res.data;
    },
    enabled: !!activeSessionId,
  });

  // Create session mutation
  const createSessionMutation = useMutation({
    mutationFn: async () => {
      const res = await apiClient.post('/api/sessions', { title: 'New Conversation', language });
      return res.data;
    },
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['sessions'] });
      setActiveSessionId(data.id);
    },
  });

  // Set initial session if none selected
  useEffect(() => {
    if (sessions.length > 0 && !activeSessionId) {
      setActiveSessionId(sessions[0].id);
    }
  }, [sessions, activeSessionId]);

  // Scroll to bottom when messages change
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [activeSession?.messages, streamedResponse]);

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim() || !activeSessionId || isStreaming) return;

    const question = input.trim();
    setInput('');
    setIsStreaming(true);
    setStreamedResponse('');
    setActiveCitations([]);
    setSelectedCitation(null);

    // Optimistically add user message (or just wait for stream to start, but better to show immediately)
    // For simplicity, we just rely on the query invalidation after done, but while streaming we show the current state + streaming state
    
    try {
      const token = localStorage.getItem('access_token');
      const baseUrl = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';
      
      const sse = new EventSource(
        `${baseUrl}/api/sessions/${activeSessionId}/ask?access_token=${token}&Question=${encodeURIComponent(question)}&Language=${language}`
      );

      let currentResponse = '';

      sse.addEventListener('starter', (e) => {
        const data = JSON.parse(e.data);
        console.log('Starter:', data);
      });

      sse.addEventListener('token', (e) => {
        const data = JSON.parse(e.data);
        currentResponse += data.text;
        setStreamedResponse(currentResponse);
      });

      sse.addEventListener('citation', (e) => {
        const data = JSON.parse(e.data);
        setActiveCitations(prev => [...prev, data]);
      });

      sse.addEventListener('done', () => {
        sse.close();
        setIsStreaming(false);
        queryClient.invalidateQueries({ queryKey: ['session', activeSessionId] });
      });

      sse.addEventListener('error', (e) => {
        console.error('SSE Error:', e);
        sse.close();
        setIsStreaming(false);
        queryClient.invalidateQueries({ queryKey: ['session', activeSessionId] });
      });

    } catch (err) {
      console.error(err);
      setIsStreaming(false);
    }
  };

  return (
    <div className="flex h-[calc(100vh-8rem)] gap-6">
      {/* Sidebar - Sessions */}
      <Card className="w-64 flex flex-col overflow-hidden">
        <div className="p-4 border-b">
          <Button 
            className="w-full" 
            onClick={() => createSessionMutation.mutate()}
            disabled={createSessionMutation.isPending}
          >
            <Plus className="h-4 w-4 me-2" />
            {isEn ? 'New Chat' : 'محادثة جديدة'}
          </Button>
        </div>
        <div className="flex-1 overflow-y-auto p-2 space-y-1">
          {sessions.map(s => (
            <button
              key={s.id}
              onClick={() => setActiveSessionId(s.id)}
              className={cn(
                "w-full text-start px-3 py-2 text-sm rounded-md truncate transition-colors",
                activeSessionId === s.id ? "bg-primary/10 text-primary font-medium" : "hover:bg-muted"
              )}
            >
              {s.title}
            </button>
          ))}
        </div>
      </Card>

      {/* Main Chat Area */}
      <Card className="flex-1 flex flex-col overflow-hidden">
        {isLoadingSession ? (
          <div className="flex-1 flex items-center justify-center">
            <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
          </div>
        ) : !activeSession ? (
          <div className="flex-1 flex flex-col items-center justify-center text-muted-foreground">
            <Bot className="h-12 w-12 mb-4 opacity-50" />
            <p>{isEn ? 'Select or start a conversation' : 'اختر أو ابدأ محادثة'}</p>
          </div>
        ) : (
          <>
            <div className="flex-1 overflow-y-auto p-6 space-y-6">
              {activeSession.messages.map((msg, i) => (
                <div key={i} className={cn("flex gap-4 max-w-[80%]", msg.role === 'user' ? "ms-auto flex-row-reverse" : "")}>
                  <div className={cn("h-8 w-8 rounded-full flex items-center justify-center shrink-0", msg.role === 'user' ? "bg-primary text-primary-foreground" : "bg-muted")}>
                    {msg.role === 'user' ? <User className="h-5 w-5" /> : <Bot className="h-5 w-5" />}
                  </div>
                  <div className={cn("rounded-lg p-4", msg.role === 'user' ? "bg-primary text-primary-foreground" : "bg-muted")}>
                    <p className="whitespace-pre-wrap leading-relaxed">{msg.content}</p>
                    {msg.citations && msg.citations.length > 0 && (
                      <div className="mt-3 flex flex-wrap gap-2">
                        {msg.citations.map((c, ci) => (
                          <button 
                            key={ci}
                            onClick={() => setSelectedCitation(c)}
                            className="inline-flex items-center text-xs bg-background/50 text-foreground border rounded-md px-2 py-1 hover:bg-background"
                          >
                            <FileText className="h-3 w-3 me-1" />
                            [{ci + 1}]
                          </button>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              ))}
              
              {/* Streaming placeholder */}
              {isStreaming && (
                <div className="flex gap-4 max-w-[80%]">
                  <div className="h-8 w-8 rounded-full bg-muted flex items-center justify-center shrink-0">
                    <Bot className="h-5 w-5" />
                  </div>
                  <div className="rounded-lg p-4 bg-muted w-full">
                    <p className="whitespace-pre-wrap leading-relaxed">
                      {streamedResponse || (isEn ? 'Thinking...' : 'جاري التفكير...')}
                      <span className="inline-block w-2 h-4 ms-1 bg-primary animate-pulse" />
                    </p>
                    {activeCitations.length > 0 && (
                      <div className="mt-3 flex flex-wrap gap-2">
                        {activeCitations.map((c, ci) => (
                          <button 
                            key={ci}
                            onClick={() => setSelectedCitation(c)}
                            className="inline-flex items-center text-xs bg-background/50 text-foreground border rounded-md px-2 py-1 hover:bg-background"
                          >
                            <FileText className="h-3 w-3 me-1" />
                            [{ci + 1}]
                          </button>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              )}
              <div ref={messagesEndRef} />
            </div>

            <div className="p-4 border-t bg-background">
              <form onSubmit={handleSend} className="flex gap-2">
                <Input
                  value={input}
                  onChange={(e) => setInput(e.target.value)}
                  placeholder={isEn ? "Ask HR Copilot..." : "اسأل مساعد الموارد البشرية..."}
                  disabled={isStreaming}
                  className="flex-1"
                />
                <Button type="submit" disabled={!input.trim() || isStreaming}>
                  <Send className="h-4 w-4" />
                </Button>
              </form>
            </div>
          </>
        )}
      </Card>

      {/* Citations Panel */}
      {selectedCitation && (
        <Card className="w-80 flex flex-col overflow-hidden shadow-xl border-primary/20">
          <div className="p-3 border-b flex justify-between items-center bg-muted/50">
            <h3 className="font-semibold text-sm flex items-center">
              <FileText className="h-4 w-4 me-2 text-primary" />
              {isEn ? 'Source Document' : 'المستند المصدر'}
            </h3>
            <Button variant="ghost" size="icon" className="h-6 w-6" onClick={() => setSelectedCitation(null)}>
              <X className="h-4 w-4" />
            </Button>
          </div>
          <div className="p-4 overflow-y-auto flex-1 space-y-4 text-sm">
            <div>
              <span className="text-muted-foreground text-xs">{isEn ? 'Document ID' : 'معرف المستند'}</span>
              <p className="font-medium break-all">{selectedCitation.DocumentId}</p>
            </div>
            {selectedCitation.Section && (
              <div>
                <span className="text-muted-foreground text-xs">{isEn ? 'Section' : 'القسم'}</span>
                <p className="font-medium">{selectedCitation.Section}</p>
              </div>
            )}
            <div>
              <span className="text-muted-foreground text-xs">{isEn ? 'Content' : 'المحتوى'}</span>
              <p className="mt-1 leading-relaxed bg-muted/30 p-3 rounded-md border text-xs">
                {selectedCitation.Text}
              </p>
            </div>
          </div>
        </Card>
      )}
    </div>
  );
}
