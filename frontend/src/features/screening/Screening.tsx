import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery, useMutation } from '@tanstack/react-query';
import { Play, Users, FileText, CheckCircle2, ChevronRight, Loader2 } from 'lucide-react';
import { apiClient } from '@/api/client';
import type { DocumentListResponse, ScreeningRequest } from '@/types/api';
import { useLanguage } from '@/hooks/useLanguage';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';

export function Screening() {
  const navigate = useNavigate();
  const { language } = useLanguage();
  const isEn = language === 'en';

  const [step, setStep] = useState(1);
  const [selectedDocs, setSelectedDocs] = useState<string[]>([]);
  
  // Default values for quick demo
  const [roleTitle, setRoleTitle] = useState('Senior Software Engineer');
  const [roleDesc, setRoleDesc] = useState('Looking for an experienced engineer to build scalable web applications.');
  
  const { data: docsResponse, isLoading: isLoadingDocs } = useQuery<DocumentListResponse>({
    queryKey: ['documents'],
    queryFn: async () => {
      const res = await apiClient.get('/api/documents');
      return res.data;
    },
  });

  const startScreeningMutation = useMutation({
    mutationFn: async (payload: ScreeningRequest) => {
      const res = await apiClient.post('/api/workflows/screening', payload);
      return res.data;
    },
    onSuccess: (data) => {
      navigate(`/app/runs/${data.run_id}`);
    },
  });

  const readyDocs = docsResponse?.documents.filter(d => d.status === 'Ready') || [];

  const handleStart = () => {
    const payload: ScreeningRequest = {
      role: {
        title: roleTitle,
        description: roleDesc,
        competencies: [
          { name: 'System Design', description: 'Ability to design scalable systems.' },
          { name: 'React Proficiency', description: 'Deep knowledge of React and hooks.' }
        ]
      },
      rubric: {
        name: 'Standard Engineering Rubric',
        dimensions: [
          { id: 'dim-1', name: 'Technical Depth', description: 'Depth of technical knowledge', maxScore: 10, weight: 0.6 },
          { id: 'dim-2', name: 'Communication', description: 'Clarity of communication', maxScore: 10, weight: 0.4 }
        ]
      },
      candidates: selectedDocs.map(id => ({
        id: id,
        name: id,
        documentId: id
      }))
    };
    startScreeningMutation.mutate(payload);
  };

  const toggleDoc = (id: string) => {
    setSelectedDocs(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]);
  };

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      <div>
        <h2 className="text-3xl font-bold tracking-tight">
          {isEn ? 'Screen Candidates' : 'فحص المرشحين'}
        </h2>
        <p className="text-muted-foreground mt-1">
          {isEn 
            ? 'Launch an agentic workflow to screen resumes against a job rubric.' 
            : 'بدء مسار عمل يعتمد على الذكاء الاصطناعي لفحص السير الذاتية.'}
        </p>
      </div>

      <div className="flex items-center justify-between mb-8">
        <div className={`flex items-center gap-2 ${step >= 1 ? 'text-primary' : 'text-muted-foreground'}`}>
          <div className={`h-8 w-8 rounded-full flex items-center justify-center border-2 ${step >= 1 ? 'border-primary bg-primary/10' : 'border-muted'}`}>1</div>
          <span className="font-medium">{isEn ? 'Job Details' : 'تفاصيل الوظيفة'}</span>
        </div>
        <ChevronRight className="h-5 w-5 text-muted-foreground" />
        <div className={`flex items-center gap-2 ${step >= 2 ? 'text-primary' : 'text-muted-foreground'}`}>
          <div className={`h-8 w-8 rounded-full flex items-center justify-center border-2 ${step >= 2 ? 'border-primary bg-primary/10' : 'border-muted'}`}>2</div>
          <span className="font-medium">{isEn ? 'Select Candidates' : 'اختر المرشحين'}</span>
        </div>
        <ChevronRight className="h-5 w-5 text-muted-foreground" />
        <div className={`flex items-center gap-2 ${step >= 3 ? 'text-primary' : 'text-muted-foreground'}`}>
          <div className={`h-8 w-8 rounded-full flex items-center justify-center border-2 ${step >= 3 ? 'border-primary bg-primary/10' : 'border-muted'}`}>3</div>
          <span className="font-medium">{isEn ? 'Review & Launch' : 'مراجعة وبدء'}</span>
        </div>
      </div>

      {step === 1 && (
        <Card>
          <CardHeader>
            <CardTitle>{isEn ? 'Job Profile' : 'الملف الوظيفي'}</CardTitle>
            <CardDescription>{isEn ? 'Define the role and requirements.' : 'حدد الوظيفة والمتطلبات.'}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="space-y-2">
              <Label>{isEn ? 'Job Title' : 'المسمى الوظيفي'}</Label>
              <Input value={roleTitle} onChange={e => setRoleTitle(e.target.value)} />
            </div>
            <div className="space-y-2">
              <Label>{isEn ? 'Description' : 'الوصف'}</Label>
              <textarea 
                className="flex w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring min-h-[100px]"
                value={roleDesc}
                onChange={e => setRoleDesc(e.target.value)}
              />
            </div>
            <div className="pt-4 flex justify-end">
              <Button onClick={() => setStep(2)}>{isEn ? 'Next: Candidates' : 'التالي: المرشحون'}</Button>
            </div>
          </CardContent>
        </Card>
      )}

      {step === 2 && (
        <Card>
          <CardHeader>
            <CardTitle>{isEn ? 'Select Candidates' : 'اختر المرشحين'}</CardTitle>
            <CardDescription>{isEn ? 'Choose resumes to screen from the knowledge base.' : 'اختر السير الذاتية للفحص من قاعدة المعرفة.'}</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoadingDocs ? (
              <div className="flex justify-center p-8"><Loader2 className="animate-spin" /></div>
            ) : (
              <div className="grid gap-3 sm:grid-cols-2">
                {readyDocs.map(doc => (
                  <div 
                    key={doc.id}
                    onClick={() => toggleDoc(doc.id)}
                    className={`p-4 border rounded-lg cursor-pointer transition-colors flex items-start gap-3 ${selectedDocs.includes(doc.id) ? 'bg-primary/5 border-primary' : 'hover:bg-muted/50'}`}
                  >
                    <div className="mt-0.5">
                      {selectedDocs.includes(doc.id) ? <CheckCircle2 className="h-5 w-5 text-primary" /> : <div className="h-5 w-5 border rounded-full" />}
                    </div>
                    <div>
                      <h4 className="font-medium text-sm line-clamp-1">{doc.title}</h4>
                      <p className="text-xs text-muted-foreground mt-1">{isEn ? 'Language:' : 'اللغة:'} {doc.language.toUpperCase()}</p>
                    </div>
                  </div>
                ))}
              </div>
            )}
            
            <div className="pt-6 flex justify-between">
              <Button variant="outline" onClick={() => setStep(1)}>{isEn ? 'Back' : 'رجوع'}</Button>
              <Button onClick={() => setStep(3)} disabled={selectedDocs.length === 0}>
                {isEn ? 'Next: Review' : 'التالي: مراجعة'} ({selectedDocs.length})
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {step === 3 && (
        <Card>
          <CardHeader>
            <CardTitle>{isEn ? 'Ready to Launch' : 'جاهز للبدء'}</CardTitle>
            <CardDescription>{isEn ? 'Review the workflow parameters before starting.' : 'راجع معايير سير العمل قبل البدء.'}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-6">
            <div className="grid grid-cols-2 gap-4 text-sm">
              <div className="space-y-1">
                <span className="text-muted-foreground">{isEn ? 'Role Title' : 'المسمى الوظيفي'}</span>
                <p className="font-medium">{roleTitle}</p>
              </div>
              <div className="space-y-1">
                <span className="text-muted-foreground">{isEn ? 'Candidates' : 'المرشحون'}</span>
                <p className="font-medium flex items-center">
                  <Users className="h-4 w-4 me-2 text-primary" />
                  {selectedDocs.length} {isEn ? 'selected' : 'محدد'}
                </p>
              </div>
            </div>

            <div className="bg-muted p-4 rounded-lg flex items-start gap-4">
              <Play className="h-8 w-8 text-primary shrink-0" />
              <div>
                <h4 className="font-semibold">{isEn ? 'Agentic Workflow' : 'مسار العمل الآلي'}</h4>
                <p className="text-sm text-muted-foreground mt-1">
                  {isEn 
                    ? 'This will launch the multi-agent screening process. Agents will extract details, redact PII, evaluate against the rubric, and draft a final summary. The draft will be routed to the Approvals queue.' 
                    : 'سيؤدي هذا إلى بدء عملية الفحص متعددة الوكلاء. سيقوم الوكلاء باستخراج التفاصيل، وتقييمها مقابل المعايير، وصياغة ملخص نهائي. سيتم توجيه المسودة إلى قائمة الموافقات.'}
                </p>
              </div>
            </div>

            <div className="pt-4 flex justify-between">
              <Button variant="outline" onClick={() => setStep(2)}>{isEn ? 'Back' : 'رجوع'}</Button>
              <Button onClick={handleStart} disabled={startScreeningMutation.isPending} className="bg-green-600 hover:bg-green-700">
                {startScreeningMutation.isPending && <Loader2 className="animate-spin h-4 w-4 me-2" />}
                {!startScreeningMutation.isPending && <Play className="h-4 w-4 me-2" />}
                {isEn ? 'Launch Screening' : 'بدء الفحص'}
              </Button>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  );
}
