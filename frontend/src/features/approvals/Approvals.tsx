import React, { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { format } from 'date-fns';
import { motion, AnimatePresence } from 'framer-motion';
import { Loader2, CheckSquare, XCircle, CheckCircle, Edit3, Eye, Code, Users, Award, MessageSquare, ArrowLeft, FileCheck, FileX, FileText, Send, Building } from 'lucide-react';
import { apiClient } from '@/api/client';
import type { Approval } from '@/types/api';
import { useLanguage } from '@/hooks/useLanguage';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';

/* ── Score colour helper ── */
function scoreColor(score: number): string {
  if (score >= 7) return 'bg-green-500';
  if (score >= 5) return 'bg-yellow-500';
  return 'bg-red-500';
}
function scoreBg(score: number): string {
  if (score >= 7) return 'bg-green-500/10 text-green-700 border-green-200';
  if (score >= 5) return 'bg-yellow-500/10 text-yellow-700 border-yellow-200';
  return 'bg-red-500/10 text-red-700 border-red-200';
}

function parseCandidateName(id: string | undefined): string {
  if (!id) return '';
  // Try extracting name from candidate ID patterns like "uuid-Name_Surname" or "name.pdf"
  // First try: parts after the 4th dash segment (UUID has 5 segments)
  const parts = id.split('-');
  if (parts.length >= 5) {
    // UUID format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx-filename
    const namePart = parts.slice(5).join('-');
    if (namePart) {
      const cleaned = namePart.replace(/\.(pdf|docx|txt|json)$/i, '').replace(/[_-]/g, ' ').trim();
      if (cleaned.length > 1) return cleaned.split(' ').map(w => w.charAt(0).toUpperCase() + w.slice(1).toLowerCase()).join(' ');
    }
  }
  // Fallback: try to extract filename-like patterns
  const match = id.match(/[a-zA-Z][a-zA-Z_\- ]{2,}/);
  if (match) {
    const cleaned = match[0].replace(/[_-]/g, ' ').trim();
    return cleaned.split(' ').map(w => w.charAt(0).toUpperCase() + w.slice(1).toLowerCase()).join(' ');
  }
  // Last fallback: show truncated ID
  return `Candidate ${id.substring(0, 8)}`;
}

/* ── CV Viewer Modal ── */
function CvViewerModal({ candidateId, onClose, isEn }: { candidateId: string; onClose: () => void; isEn: boolean }) {
  const { data, isLoading, error } = useQuery({
    queryKey: ['document', candidateId],
    queryFn: async () => {
      // Strategy 1: Try by-title endpoint (handles contains-match on server)
      try {
        const res = await apiClient.get(`/api/documents/by-title/${encodeURIComponent(candidateId)}`);
        if (res.data?.full_text) return res.data;
      } catch {}
      
      // Strategy 2: Try direct document ID lookup (if candidateId is a GUID)
      try {
        const res = await apiClient.get(`/api/documents/${candidateId}`);
        if (res.data) {
          // Reconstruct full_text from chunks
          const chunks = res.data.chunks || [];
          const fullText = chunks.map((c: any) => c.text_preview || '').join('\n\n');
          return { ...res.data, full_text: fullText || 'Document found but no content available.' };
        }
      } catch {}
      
      // Strategy 3: List all documents and find one whose title contains part of the candidateId
      try {
        const res = await apiClient.get('/api/documents');
        const docs = res.data?.documents || [];
        const match = docs.find((d: any) => 
          candidateId.includes(d.id) || 
          d.id.includes(candidateId) ||
          d.title?.toLowerCase().includes(candidateId.toLowerCase()) ||
          candidateId.toLowerCase().includes(d.title?.toLowerCase())
        );
        if (match) {
          const detailRes = await apiClient.get(`/api/documents/${match.id}`);
          const chunks = detailRes.data?.chunks || [];
          const fullText = chunks.map((c: any) => c.text_preview || '').join('\n\n');
          return { ...detailRes.data, full_text: fullText || 'Document found but no content available.' };
        }
      } catch {}
      
      throw new Error('CV not found');
    }
  });

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
      <motion.div
        initial={{ opacity: 0, scale: 0.95, y: 20 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        exit={{ opacity: 0, scale: 0.95, y: 20 }}
        className="bg-background rounded-xl shadow-2xl w-full max-w-2xl max-h-[90vh] flex flex-col overflow-hidden border"
      >
        <div className="px-6 py-4 border-b flex justify-between items-center bg-muted/30">
          <h3 className="font-semibold text-lg flex items-center gap-2">
            <FileText className="w-5 h-5 text-primary" />
            {isEn ? 'Original Candidate CV' : 'السيرة الذاتية الأصلية للمرشح'}
          </h3>
          <button onClick={onClose} className="p-2 hover:bg-muted rounded-full transition-colors">
            <XCircle className="w-5 h-5 text-muted-foreground" />
          </button>
        </div>
        <div className="flex-1 overflow-y-auto p-6 bg-muted/10">
          {isLoading ? (
            <div className="flex flex-col items-center justify-center h-40 text-muted-foreground">
              <Loader2 className="w-8 h-8 animate-spin mb-4" />
              <p>{isEn ? 'Fetching document...' : 'جاري جلب المستند...'}</p>
            </div>
          ) : error ? (
            <div className="flex flex-col items-center justify-center h-40 text-red-500">
              <FileX className="w-8 h-8 mb-4" />
              <p>{isEn ? 'Could not load CV. The document might have been removed.' : 'تعذر تحميل السيرة الذاتية. قد يكون المستند غير متاح.'}</p>
            </div>
          ) : (
            <div className="prose dark:prose-invert max-w-none text-sm whitespace-pre-wrap font-mono bg-background p-6 rounded-lg border shadow-sm">
              {data?.full_text || (isEn ? 'No content' : 'لا يوجد محتوى')}
            </div>
          )}
        </div>
        <div className="px-6 py-4 border-t bg-muted/30 flex justify-end">
          <Button onClick={onClose}>{isEn ? 'Close' : 'إغلاق'}</Button>
        </div>
      </motion.div>
    </div>
  );
}

/* ── Improved Shortlist Preview ── */
function ShortlistPreview({ payload, isEn }: { payload: any; isEn: boolean }) {
  const [viewingCvId, setViewingCvId] = useState<string | null>(null);

  if (!payload || !payload.Entries) return null;
  const entries = payload.Entries;
  const maxScore = 10;
  const avgScore = entries.length > 0
    ? (entries.reduce((s: number, e: any) => s + (e.TotalScore || 0), 0) / entries.length).toFixed(1)
    : '0';

  const containerVariants = {
    hidden: { opacity: 0 },
    show: {
      opacity: 1,
      transition: { staggerChildren: 0.1 }
    }
  };

  const itemVariants = {
    hidden: { opacity: 0, y: 15 },
    show: { opacity: 1, y: 0, transition: { stiffness: 300, damping: 24 } }
  } as any;

  return (
    <div className="space-y-5 relative">
      {/* ── Summary stats banner ── */}
      <motion.div initial={{ opacity: 0, y: -10 }} animate={{ opacity: 1, y: 0 }} className="grid grid-cols-3 gap-3">
        <div className="bg-primary/5 rounded-lg p-3 text-center border">
          <Users className="w-5 h-5 mx-auto mb-1 text-primary" />
          <div className="text-xl font-bold">{entries.length}</div>
          <div className="text-[11px] text-muted-foreground">{isEn ? 'Candidates' : 'مرشحين'}</div>
        </div>
        <div className="bg-primary/5 rounded-lg p-3 text-center border">
          <Award className="w-5 h-5 mx-auto mb-1 text-primary" />
          <div className="text-xl font-bold">{avgScore}<span className="text-sm text-muted-foreground">/{maxScore}</span></div>
          <div className="text-[11px] text-muted-foreground">{isEn ? 'Avg Score' : 'متوسط النتيجة'}</div>
        </div>
        <div className="bg-primary/5 rounded-lg p-3 text-center border">
          <MessageSquare className="w-5 h-5 mx-auto mb-1 text-primary" />
          <div className="text-xl font-bold">{entries.reduce((t: number, e: any) => t + (e.Probes?.length || 0), 0)}</div>
          <div className="text-[11px] text-muted-foreground">{isEn ? 'Interview Qs' : 'أسئلة مقابلة'}</div>
        </div>
      </motion.div>

      {/* ── Role label ── */}
      <div className="flex items-center gap-2">
        <span className="text-sm font-semibold">{isEn ? 'Role:' : 'الدور الوظيفي:'}</span>
        <Badge variant="secondary" className="text-sm">{payload.Role}</Badge>
      </div>

      {/* ── Candidate cards ── */}
      <motion.div variants={containerVariants} initial="hidden" animate="show" className="space-y-4">
        {entries.map((entry: any, i: number) => {
          const score = entry.totalScore || entry.TotalScore || 0;
          const candidateId = entry.candidateId || entry.CandidateId || '';
          const pct = Math.min((score / maxScore) * 100, 100);
          return (
            <motion.div variants={itemVariants} key={i} className="rounded-lg border shadow-sm overflow-hidden bg-background">
              {/* Card header */}
              <div className="flex items-center justify-between bg-muted/40 px-4 py-3 border-b">
                <div className="flex items-center gap-3">
                  <span className="flex items-center justify-center w-8 h-8 rounded-full bg-primary text-white font-bold text-sm">
                    {entry.rank || entry.Rank || i + 1}
                  </span>
                  <div>
                    <span className="font-semibold text-base">
                      {parseCandidateName(candidateId)}
                    </span>
                    <span className="block text-xs text-muted-foreground">
                      ID: {candidateId.split('-')[0]}...
                    </span>
                  </div>
                </div>
                <div className="flex items-center gap-3">
                  <Button size="sm" variant="outline" className="h-8 text-xs gap-1.5" onClick={() => setViewingCvId(candidateId)}>
                    <Eye className="w-3.5 h-3.5" />
                    {isEn ? 'View CV' : 'عرض السيرة'}
                  </Button>
                  <div className={`flex items-center gap-2 px-3 py-1 rounded-full border text-sm font-bold ${scoreBg(score)}`}>
                    {Number(score).toFixed(1)} / {maxScore}
                  </div>
                </div>
              </div>

              <div className="p-4 space-y-4">
                {/* Score bar */}
                <div>
                  <div className="flex justify-between text-xs mb-1">
                    <span className="text-muted-foreground">{isEn ? 'Overall Score' : 'النتيجة الإجمالية'}</span>
                    <span className="font-medium">{pct.toFixed(0)}%</span>
                  </div>
                  <div className="h-2 bg-muted rounded-full overflow-hidden">
                    <motion.div 
                      initial={{ width: 0 }} 
                      animate={{ width: `${pct}%` }} 
                      transition={{ duration: 1, ease: "easeOut", delay: 0.2 + (i * 0.1) }}
                      className={`h-full rounded-full ${scoreColor(score)}`} 
                    />
                  </div>
                </div>

                {/* Summary */}
                <div>
                  <h5 className="text-xs font-semibold uppercase text-muted-foreground mb-1">
                    {isEn ? 'AI Assessment' : 'تقييم الذكاء الاصطناعي'}
                  </h5>
                  <p className="text-sm border-l-2 border-primary pl-3 py-1 text-muted-foreground italic whitespace-pre-wrap">
                    {entry.Summary}
                  </p>
                </div>

                {/* Interview probes */}
                {entry.Probes && entry.Probes.length > 0 && (
                  <div>
                    <h5 className="text-xs font-semibold uppercase text-muted-foreground mb-2">
                      {isEn ? 'Suggested Interview Questions' : 'أسئلة مقابلة مقترحة'}
                    </h5>
                    <ol className="space-y-2 list-none">
                      {entry.Probes.map((probe: any, j: number) => (
                        <li key={j} className="text-sm bg-muted/30 p-3 rounded-lg border flex gap-3">
                          <span className="flex-shrink-0 flex items-center justify-center w-6 h-6 rounded-full bg-primary/10 text-primary text-xs font-bold">
                            {j + 1}
                          </span>
                          <div>
                            <span className="font-semibold text-primary text-xs block mb-0.5">{probe.Competency}</span>
                            <span className="text-muted-foreground">{probe.Question}</span>
                          </div>
                        </li>
                      ))}
                    </ol>
                  </div>
                )}
              </div>
            </motion.div>
          );
        })}
      </motion.div>

      {/* Rationale */}
      {payload.Rationale && (
        <motion.div variants={itemVariants} className="p-4 bg-blue-50 dark:bg-blue-950/30 rounded-lg border border-blue-200 dark:border-blue-900 text-sm">
          <span className="font-semibold text-blue-700 dark:text-blue-400">{isEn ? 'AI Rationale:' : 'تبرير الذكاء الاصطناعي:'} </span>
          <span className="text-blue-900 dark:text-blue-300">{payload.Rationale}</span>
        </motion.div>
      )}

      {/* CV Modal Portal */}
      <AnimatePresence>
        {viewingCvId && (
          <CvViewerModal candidateId={viewingCvId} isEn={isEn} onClose={() => setViewingCvId(null)} />
        )}
      </AnimatePresence>
    </div>
  );
}

/* ── Decision Result Screen ── */
function DecisionResult({
  decision,
  approval,
  comment,
  isEn,
  onDismiss,
}: {
  decision: string;
  approval: Approval;
  comment: string;
  isEn: boolean;
  onDismiss: () => void;
}) {
  const isApproved = decision === 'approve' || decision === 'edit_and_approve';
  const payload = approval.payload;
  const entries = payload?.Entries || [];

  // ATS Sync Mock State
  const [syncStep, setSyncStep] = useState(0);

  useEffect(() => {
    if (isApproved) {
      const timer1 = setTimeout(() => setSyncStep(1), 800);
      const timer2 = setTimeout(() => setSyncStep(2), 2000);
      const timer3 = setTimeout(() => setSyncStep(3), 3200);
      return () => { clearTimeout(timer1); clearTimeout(timer2); clearTimeout(timer3); };
    }
  }, [isApproved]);

  return (
    <Card className="h-full overflow-hidden relative">
      <CardContent className="p-8 space-y-6">
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} className="text-center py-4">
          {isApproved ? (
            <motion.div 
              initial={{ scale: 0 }} 
              animate={{ scale: 1 }} 
              transition={{ type: "spring", damping: 12 }}
              className="inline-flex items-center justify-center w-20 h-20 rounded-full bg-green-100 dark:bg-green-900/30 mb-4"
            >
              <FileCheck className="w-10 h-10 text-green-600 dark:text-green-400" />
            </motion.div>
          ) : (
            <motion.div 
              initial={{ scale: 0 }} 
              animate={{ scale: 1 }} 
              transition={{ type: "spring", damping: 12 }}
              className="inline-flex items-center justify-center w-20 h-20 rounded-full bg-red-100 dark:bg-red-900/30 mb-4"
            >
              <FileX className="w-10 h-10 text-red-600 dark:text-red-400" />
            </motion.div>
          )}
          <h2 className="text-2xl font-bold mb-1">
            {isApproved
              ? (isEn ? 'Shortlist Approved & Published' : 'تم اعتماد ونشر القائمة المختصرة')
              : (isEn ? 'Shortlist Rejected' : 'تم رفض القائمة المختصرة')}
          </h2>
          <p className="text-muted-foreground text-sm">
            {isApproved
              ? (isEn
                  ? 'The candidate shortlist has been finalized and saved. The screening workflow is now complete.'
                  : 'تم اعتماد القائمة المختصرة وحفظها. اكتملت عملية الفحص بنجاح.')
              : (isEn
                  ? 'The shortlist has been rejected. The screening workflow has been marked as failed.'
                  : 'تم رفض القائمة المختصرة. تم تسجيل عملية الفحص كفاشلة.')}
          </p>
        </motion.div>

        {/* ATS Syncing Mock (Only if approved) */}
        {isApproved && (
          <div className="bg-muted/30 p-5 rounded-xl border border-primary/10">
            <h4 className="text-sm font-semibold mb-4 flex items-center gap-2">
              <Building className="w-4 h-4 text-primary" />
              {isEn ? 'System Integration Status' : 'حالة تكامل الأنظمة'}
            </h4>
            <div className="space-y-4">
              <div className="flex items-center gap-3">
                {syncStep >= 1 ? <CheckCircle className="w-5 h-5 text-green-500" /> : <Loader2 className="w-5 h-5 animate-spin text-muted-foreground" />}
                <span className={`text-sm ${syncStep >= 1 ? 'text-foreground font-medium' : 'text-muted-foreground'}`}>
                  {isEn ? 'Saving final scores to database...' : 'حفظ النتائج النهائية في قاعدة البيانات...'}
                </span>
              </div>
              <div className="flex items-center gap-3">
                {syncStep >= 2 ? <CheckCircle className="w-5 h-5 text-green-500" /> : (syncStep >= 1 ? <Loader2 className="w-5 h-5 animate-spin text-primary" /> : <div className="w-5 h-5 rounded-full border-2 border-muted" />)}
                <span className={`text-sm ${syncStep >= 2 ? 'text-foreground font-medium' : (syncStep >= 1 ? 'text-foreground' : 'text-muted-foreground')}`}>
                  {isEn ? 'Syncing candidates with Workday ATS...' : 'مزامنة المرشحين مع نظام إدارة التوظيف (Workday)...'}
                </span>
              </div>
              <div className="flex items-center gap-3">
                {syncStep >= 3 ? <CheckCircle className="w-5 h-5 text-green-500" /> : (syncStep >= 2 ? <Loader2 className="w-5 h-5 animate-spin text-primary" /> : <div className="w-5 h-5 rounded-full border-2 border-muted" />)}
                <span className={`text-sm ${syncStep >= 3 ? 'text-foreground font-medium' : (syncStep >= 2 ? 'text-foreground' : 'text-muted-foreground')}`}>
                  {isEn ? `Scheduling ${entries.length} interview invitation emails...` : `جدولة إرسال ${entries.length} رسالة بريد إلكتروني لدعوات المقابلة...`}
                </span>
              </div>
            </div>
            
            <AnimatePresence>
              {syncStep >= 3 && (
                <motion.div 
                  initial={{ opacity: 0, height: 0, marginTop: 0 }} 
                  animate={{ opacity: 1, height: 'auto', marginTop: 16 }} 
                  className="bg-green-500/10 text-green-700 dark:text-green-400 border border-green-200 dark:border-green-900/50 p-3 rounded-lg text-sm flex items-center gap-2"
                >
                  <Send className="w-4 h-4" />
                  {isEn ? 'All integrations completed successfully.' : 'تمت جميع عمليات المزامنة بنجاح.'}
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        )}

        <div className="pt-4 border-t flex justify-center">
          <Button onClick={onDismiss} variant="outline" className="gap-2">
            <ArrowLeft className="w-4 h-4" />
            {isEn ? 'Back to Approvals Queue' : 'العودة إلى قائمة الموافقات'}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

/* ── Main Component ── */
export function Approvals() {
  const { language } = useLanguage();
  const isEn = language === 'en';
  const queryClient = useQueryClient();

  const [selectedApproval, setSelectedApproval] = useState<Approval | null>(null);
  const [comment, setComment] = useState('');
  const [editedPayload, setEditedPayload] = useState('');
  const [viewMode, setViewMode] = useState<'preview' | 'raw'>('preview');

  const [completedDecision, setCompletedDecision] = useState<{
    decision: string;
    approval: Approval;
    comment: string;
  } | null>(null);

  const { data, isLoading } = useQuery<{ pending: Approval[] }>({
    queryKey: ['approvals'],
    queryFn: async () => {
      const res = await apiClient.get('/api/approvals/pending');
      return res.data;
    }
  });

  const decideMutation = useMutation({
    mutationFn: async ({ id, decision }: { id: string, decision: string }) => {
      const res = await apiClient.post(`/api/approvals/${id}/decide`, {
        decision,
        comment: comment || null,
        editedPayloadJson: decision === 'edit_and_approve' ? editedPayload : null
      });
      return res.data;
    },
    onSuccess: (_data, variables) => {
      if (selectedApproval) {
        setCompletedDecision({
          decision: variables.decision,
          approval: selectedApproval,
          comment,
        });
      }
      queryClient.invalidateQueries({ queryKey: ['approvals'] });
      setSelectedApproval(null);
      setComment('');
      setEditedPayload('');
      setViewMode('preview');
    }
  });

  const handleSelect = (app: Approval) => {
    setSelectedApproval(app);
    setEditedPayload(JSON.stringify(app.payload, null, 2));
    setComment('');
    setViewMode('preview');
    setCompletedDecision(null);
  };

  const dismissResult = () => {
    setCompletedDecision(null);
  };

  const pending = data?.pending || [];

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-3xl font-bold tracking-tight">
          {isEn ? 'Approvals Queue' : 'قائمة الموافقات'}
        </h2>
        <p className="text-muted-foreground mt-1">
          {isEn
            ? 'Review and authorize agent actions requiring human oversight.'
            : 'راجع وصادق على إجراءات الوكلاء التي تتطلب إشرافاً بشرياً.'}
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* ── Left sidebar: pending list ── */}
        <div className="md:col-span-1 space-y-4">
          <Card>
            <CardHeader>
              <CardTitle className="text-lg">{isEn ? 'Pending Action' : 'في انتظار الإجراء'}</CardTitle>
            </CardHeader>
            <CardContent className="p-0">
              {isLoading ? (
                <div className="p-8 flex justify-center"><Loader2 className="animate-spin" /></div>
              ) : pending.length === 0 ? (
                <div className="p-8 text-center text-muted-foreground">
                  {isEn ? 'Queue is empty' : 'القائمة فارغة'}
                </div>
              ) : (
                <div className="divide-y">
                  {pending.map((app) => (
                    <button
                      key={app.approval_id}
                      onClick={() => handleSelect(app)}
                      className={`w-full text-start p-4 hover:bg-muted/50 transition-colors ${selectedApproval?.approval_id === app.approval_id ? 'bg-primary/5 border-l-4 border-l-primary' : ''}`}
                    >
                      <div className="flex justify-between items-start mb-1">
                        <span className="font-semibold text-sm truncate">{app.step}</span>
                        <span className="text-xs text-muted-foreground whitespace-nowrap ms-2">
                          {format(new Date(app.created_at), 'MMM d, h:mm a')}
                        </span>
                      </div>
                      <p className="text-xs text-muted-foreground mb-2">Run: {app.run_id.split('-')[0]}...</p>
                      <div className="flex gap-2">
                        <span className="text-[10px] bg-secondary px-1.5 py-0.5 rounded">{app.role}</span>
                      </div>
                    </button>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>
        </div>

        {/* ── Right panel: review / result ── */}
        <div className="md:col-span-2">
          <AnimatePresence mode="wait">
            {completedDecision ? (
              <motion.div key="result" initial={{ opacity: 0, scale: 0.98 }} animate={{ opacity: 1, scale: 1 }} exit={{ opacity: 0 }}>
                <DecisionResult
                  decision={completedDecision.decision}
                  approval={completedDecision.approval}
                  comment={completedDecision.comment}
                  isEn={isEn}
                  onDismiss={dismissResult}
                />
              </motion.div>
            ) : selectedApproval ? (
              <motion.div key="review" initial={{ opacity: 0, x: 20 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: -20 }} className="h-full">
                <Card className="h-full">
                  <CardHeader className="border-b">
                    <div className="flex items-center gap-3">
                      <CheckSquare className="h-6 w-6 text-primary" />
                      <div className="flex-1">
                        <CardTitle>
                          {isEn ? 'Review Candidate Shortlist' : 'مراجعة القائمة المختصرة للمرشحين'}
                        </CardTitle>
                        <CardDescription>
                          {isEn
                            ? 'The AI has evaluated all candidates and produced the ranked shortlist below. Review the scores and suggested interview questions, then approve or reject.'
                            : 'قام الذكاء الاصطناعي بتقييم جميع المرشحين وأنتج القائمة المختصرة المرتبة أدناه. راجع النتائج وأسئلة المقابلة المقترحة، ثم وافق أو ارفض.'}
                        </CardDescription>
                      </div>
                    </div>
                  </CardHeader>
                  <CardContent className="p-6 space-y-6">
                    <div>
                      <div className="flex justify-between items-center mb-3">
                        <h3 className="text-sm font-semibold">{isEn ? 'Shortlist Details' : 'تفاصيل القائمة المختصرة'}</h3>
                        <div className="flex bg-muted rounded p-1">
                          <button
                            onClick={() => setViewMode('preview')}
                            className={`text-xs flex items-center gap-1 px-3 py-1.5 rounded transition-colors ${viewMode === 'preview' ? 'bg-background shadow font-medium' : 'text-muted-foreground hover:text-foreground'}`}
                          >
                            <Eye className="w-3.5 h-3.5" /> {isEn ? 'Preview' : 'معاينة'}
                          </button>
                          <button
                            onClick={() => setViewMode('raw')}
                            className={`text-xs flex items-center gap-1 px-3 py-1.5 rounded transition-colors ${viewMode === 'raw' ? 'bg-background shadow font-medium' : 'text-muted-foreground hover:text-foreground'}`}
                          >
                            <Code className="w-3.5 h-3.5" /> {isEn ? 'Raw JSON' : 'صيغة JSON'}
                          </button>
                        </div>
                      </div>

                      {viewMode === 'preview' && selectedApproval.step === 'shortlist_publish' ? (
                        <div className="border rounded-lg p-4 bg-muted/10 max-h-[500px] overflow-y-auto">
                          <ShortlistPreview payload={selectedApproval.payload} isEn={isEn} />
                        </div>
                      ) : (
                        <>
                          <textarea
                            value={editedPayload}
                            onChange={(e) => setEditedPayload(e.target.value)}
                            className="w-full h-[300px] p-3 text-sm font-mono bg-muted rounded-md border resize-none focus:ring-1 focus:ring-primary outline-none"
                            spellCheck={false}
                          />
                          <p className="text-xs text-muted-foreground mt-2">
                            {isEn
                              ? 'You can edit this payload before approving (Edit & Approve).'
                              : 'يمكنك تعديل هذه البيانات قبل الموافقة (تعديل وموافقة).'}
                          </p>
                        </>
                      )}
                    </div>

                    <div>
                      <h3 className="text-sm font-semibold mb-2">{isEn ? 'Reviewer Comment' : 'تعليق المراجع'}</h3>
                      <textarea
                        value={comment}
                        onChange={(e) => setComment(e.target.value)}
                        placeholder={isEn ? 'Add an optional comment...' : 'أضف تعليقاً اختيارياً...'}
                        className="w-full h-[80px] p-3 text-sm bg-background rounded-md border resize-none focus:ring-1 focus:ring-primary outline-none"
                      />
                    </div>

                    <div className="pt-4 border-t space-y-3">
                      <p className="text-xs text-muted-foreground">
                        {isEn
                          ? '⚡ Approve = publish this shortlist as-is. Edit & Approve = modify then publish. Reject = discard this shortlist entirely.'
                          : '⚡ موافقة = نشر القائمة كما هي. تعديل وموافقة = تعديل ثم نشر. رفض = تجاهل القائمة بالكامل.'}
                      </p>
                      <div className="flex items-center gap-3">
                        <Button
                          variant="outline"
                          className="text-destructive hover:bg-destructive/10 hover:text-destructive"
                          disabled={decideMutation.isPending}
                          onClick={() => decideMutation.mutate({ id: selectedApproval.approval_id, decision: 'reject' })}
                        >
                          {decideMutation.isPending && decideMutation.variables?.decision === 'reject' ? <Loader2 className="h-4 w-4 me-2 animate-spin" /> : <XCircle className="h-4 w-4 me-2" />}
                          {isEn ? 'Reject' : 'رفض'}
                        </Button>
                        <div className="flex-1" />
                        <Button
                          variant="secondary"
                          disabled={decideMutation.isPending}
                          onClick={() => decideMutation.mutate({ id: selectedApproval.approval_id, decision: 'edit_and_approve' })}
                        >
                          {decideMutation.isPending && decideMutation.variables?.decision === 'edit_and_approve' ? <Loader2 className="h-4 w-4 me-2 animate-spin" /> : <Edit3 className="h-4 w-4 me-2" />}
                          {isEn ? 'Edit & Approve' : 'تعديل وموافقة'}
                        </Button>
                        <Button
                          disabled={decideMutation.isPending}
                          onClick={() => decideMutation.mutate({ id: selectedApproval.approval_id, decision: 'approve' })}
                        >
                          {decideMutation.isPending && decideMutation.variables?.decision === 'approve' ? <Loader2 className="h-4 w-4 me-2 animate-spin" /> : <CheckCircle className="h-4 w-4 me-2" />}
                          {isEn ? 'Approve As Is' : 'موافقة كما هي'}
                        </Button>
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </motion.div>
            ) : (
              <motion.div key="empty" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} className="h-full">
                <Card className="h-full flex items-center justify-center p-12 bg-muted/30">
                  <div className="text-center text-muted-foreground">
                    <CheckSquare className="h-12 w-12 mx-auto mb-4 opacity-20" />
                    <p>{isEn ? 'Select an item from the queue to review.' : 'اختر عنصراً من القائمة للمراجعة.'}</p>
                  </div>
                </Card>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </div>
    </div>
  );
}
