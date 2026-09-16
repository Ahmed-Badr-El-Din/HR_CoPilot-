import React, { useEffect, useState, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { format } from 'date-fns';
import { Loader2, ArrowLeft, Bot, Wrench, FileText, CheckCircle2, AlertCircle, Clock } from 'lucide-react';
import { apiClient } from '@/api/client';
import type { RunDetail, RunEvent } from '@/types/api';
import { useLanguage } from '@/hooks/useLanguage';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';

export function RunTrace() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { language } = useLanguage();
  const isEn = language === 'en';
  const endOfListRef = useRef<HTMLDivElement>(null);

  const [liveEvents, setLiveEvents] = useState<RunEvent[]>([]);
  
  const { data: run, isLoading } = useQuery<RunDetail>({
    queryKey: ['run', id],
    queryFn: async () => {
      const res = await apiClient.get(`/api/runs/${id}`);
      return res.data;
    },
    refetchInterval: (data) => {
      if (!data) return 5000;
      return data.status === 'Running' ? 5000 : false;
    }
  });

  // Connect to SSE for live events
  useEffect(() => {
    if (!id) return;
    
    // Clear live events when switching runs
    setLiveEvents([]);
    
    const token = localStorage.getItem('access_token');
    const baseUrl = import.meta.env.VITE_API_BASE_URL || 'http://localhost:5000';
    
    const sse = new EventSource(`${baseUrl}/api/runs/${id}/events?access_token=${token}`);
    
    sse.addEventListener('event', (e) => {
      const event: RunEvent = JSON.parse(e.data);
      setLiveEvents(prev => {
        // avoid duplicates if we also get them from REST API
        if (prev.some(p => p.created_at === event.created_at && p.text === event.text)) return prev;
        return [...prev, event];
      });
      // Scroll down
      setTimeout(() => {
        endOfListRef.current?.scrollIntoView({ behavior: 'smooth' });
      }, 100);
    });

    sse.addEventListener('error', () => {
      sse.close();
    });

    return () => sse.close();
  }, [id]);

  const allEvents = run ? [...run.events] : [];
  // Merge live events that aren't already in the REST payload
  liveEvents.forEach(le => {
    if (!allEvents.some(ae => ae.created_at === le.created_at && ae.text === le.text)) {
      allEvents.push(le);
    }
  });
  
  // Sort by time
  allEvents.sort((a, b) => new Date(a.created_at).getTime() - new Date(b.created_at).getTime());

  const getStatusIcon = (status?: string) => {
    switch (status) {
      case 'Running': return <Loader2 className="h-5 w-5 animate-spin text-warning" />;
      case 'Completed': return <CheckCircle2 className="h-5 w-5 text-success" />;
      case 'Failed': return <AlertCircle className="h-5 w-5 text-destructive" />;
      case 'AwaitingApproval': return <Clock className="h-5 w-5 text-muted-foreground" />;
      default: return null;
    }
  };

  if (isLoading) {
    return <div className="flex justify-center p-12"><Loader2 className="animate-spin h-8 w-8 text-primary" /></div>;
  }

  if (!run) return <div>Run not found</div>;

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="outline" size="icon" onClick={() => navigate('/app/runs')}>
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div>
          <h2 className="text-2xl font-bold tracking-tight flex items-center gap-3">
            {isEn ? 'Run Trace' : 'تتبع مسار العمل'} 
            <span className="font-mono text-sm text-muted-foreground mt-1">{id}</span>
          </h2>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <div className="md:col-span-2 space-y-6">
          <Card className="flex flex-col h-[600px]">
            <CardHeader className="border-b bg-muted/30 pb-4">
              <div className="flex justify-between items-center">
                <CardTitle>{isEn ? 'Live Execution Trace' : 'تتبع التنفيذ المباشر'}</CardTitle>
                <div className="flex items-center gap-2">
                  <span className="text-sm font-medium">{run.status}</span>
                  {getStatusIcon(run.status)}
                </div>
              </div>
            </CardHeader>
            <CardContent className="flex-1 overflow-y-auto p-0">
              <div className="space-y-0 relative">
                {allEvents.map((ev, i) => (
                  <div key={i} className="flex gap-4 p-4 border-b hover:bg-muted/30 transition-colors">
                    <div className="shrink-0 flex flex-col items-center">
                      <div className="h-10 w-10 rounded-full bg-primary/10 flex items-center justify-center border border-primary/20">
                        {ev.tool ? <Wrench className="h-5 w-5 text-primary" /> : <Bot className="h-5 w-5 text-primary" />}
                      </div>
                      {i !== allEvents.length - 1 && <div className="w-px h-full bg-border mt-4" />}
                    </div>
                    <div className="flex-1 space-y-1 py-1">
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-sm">
                          {ev.agent || 'System'}
                        </span>
                        <span className="text-xs text-muted-foreground">
                          {format(new Date(ev.created_at), 'HH:mm:ss.SSS')}
                        </span>
                        {ev.tool && (
                          <Badge variant="outline" className="text-xs font-mono">
                            {ev.tool}
                          </Badge>
                        )}
                      </div>
                      <p className="text-sm leading-relaxed whitespace-pre-wrap">{ev.text}</p>
                      
                      {ev.payload && (
                        <div className="mt-2 bg-muted rounded-md p-3 overflow-x-auto">
                          <pre className="text-xs font-mono text-muted-foreground">
                            {JSON.stringify(ev.payload, null, 2)}
                          </pre>
                        </div>
                      )}
                    </div>
                  </div>
                ))}
                {allEvents.length === 0 && (
                  <div className="p-8 text-center text-muted-foreground">
                    {isEn ? 'Waiting for events...' : 'في انتظار الأحداث...'}
                  </div>
                )}
                <div ref={endOfListRef} />
              </div>
            </CardContent>
          </Card>
        </div>

        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle>{isEn ? 'Details' : 'التفاصيل'}</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4 text-sm">
              <div>
                <span className="text-muted-foreground">{isEn ? 'Workflow Type' : 'نوع مسار العمل'}</span>
                <p className="font-medium">{run.kind}</p>
              </div>
              <div>
                <span className="text-muted-foreground">{isEn ? 'Owner' : 'المالك'}</span>
                <p className="font-medium">{run.owner}</p>
              </div>
              {run.started_at && (
                <div>
                  <span className="text-muted-foreground">{isEn ? 'Started' : 'تاريخ البدء'}</span>
                  <p className="font-medium">{format(new Date(run.started_at), 'MMM d, yyyy HH:mm:ss')}</p>
                </div>
              )}
              {run.finished_at && (
                <div>
                  <span className="text-muted-foreground">{isEn ? 'Finished' : 'تاريخ الانتهاء'}</span>
                  <p className="font-medium">{format(new Date(run.finished_at), 'MMM d, yyyy HH:mm:ss')}</p>
                </div>
              )}
              {run.error && (
                <div className="bg-destructive/10 text-destructive p-3 rounded-md">
                  <span className="font-semibold block mb-1">Error</span>
                  {run.error}
                </div>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{isEn ? 'Cost Metrics' : 'مقاييس التكلفة'}</CardTitle>
            </CardHeader>
            <CardContent>
              {(() => {
                const totalPrompt = allEvents.reduce((acc, ev) => acc + (ev.prompt_tokens || 0), 0);
                const totalComp = allEvents.reduce((acc, ev) => acc + (ev.completion_tokens || 0), 0);
                const totalCost = allEvents.reduce((acc, ev) => acc + (ev.cost_usd || 0), 0);

                return (
                  <div className="space-y-4">
                    <div className="flex justify-between items-center border-b pb-2">
                      <span className="text-muted-foreground text-sm">Prompt Tokens</span>
                      <span className="font-mono">{totalPrompt.toLocaleString()}</span>
                    </div>
                    <div className="flex justify-between items-center border-b pb-2">
                      <span className="text-muted-foreground text-sm">Completion Tokens</span>
                      <span className="font-mono">{totalComp.toLocaleString()}</span>
                    </div>
                    <div className="flex justify-between items-center">
                      <span className="text-muted-foreground text-sm">Total Cost</span>
                      <span className="font-mono font-semibold text-primary">
                        ${totalCost.toFixed(4)}
                      </span>
                    </div>
                  </div>
                );
              })()}
            </CardContent>
          </Card>
        </div>
      </div>
    </div>
  );
}
