import React from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Files, Activity, Database, CheckCircle2, AlertCircle } from 'lucide-react';
import { useLanguage } from '@/hooks/useLanguage';

interface CorpusStats {
  documents: number;
  ready_documents: number;
  failed_documents: number;
  languages: { language: string; documents: number }[];
}

export function Dashboard() {
  const { language } = useLanguage();
  const isEn = language === 'en';

  const { data: stats, isLoading } = useQuery<CorpusStats>({
    queryKey: ['corpus-stats'],
    queryFn: async () => {
      const res = await apiClient.get('/api/corpus/stats');
      return res.data;
    },
  });

  if (isLoading) {
    return <div className="space-y-4">
      <div className="h-32 rounded-xl bg-muted animate-pulse" />
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        {[...Array(4)].map((_, i) => <div key={i} className="h-32 rounded-xl bg-muted animate-pulse" />)}
      </div>
    </div>;
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col space-y-2">
        <h2 className="text-3xl font-bold tracking-tight">
          {isEn ? 'Welcome to HR Copilot' : 'مرحباً بك في مساعد الموارد البشرية'}
        </h2>
        <p className="text-muted-foreground">
          {isEn 
            ? 'Your AI-powered assistant for screening and document analysis.' 
            : 'مساعدك الذكي لفحص المرشحين وتحليل المستندات.'}
        </p>
      </div>

      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium">
              {isEn ? 'Total Documents' : 'إجمالي المستندات'}
            </CardTitle>
            <Files className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold">{stats?.documents || 0}</div>
            <p className="text-xs text-muted-foreground mt-1">
              {isEn ? 'Indexed in the knowledge base' : 'مفهرسة في قاعدة المعرفة'}
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium">
              {isEn ? 'Ready Documents' : 'المستندات الجاهزة'}
            </CardTitle>
            <CheckCircle2 className="h-4 w-4 text-green-500" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-green-500">{stats?.ready_documents || 0}</div>
            <p className="text-xs text-muted-foreground mt-1">
              {isEn ? 'Available for RAG' : 'متاحة للبحث والاسترجاع'}
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium">
              {isEn ? 'Failed Documents' : 'المستندات الفاشلة'}
            </CardTitle>
            <AlertCircle className="h-4 w-4 text-destructive" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-destructive">{stats?.failed_documents || 0}</div>
            <p className="text-xs text-muted-foreground mt-1">
              {isEn ? 'Failed parsing or chunking' : 'فشل في التحليل أو التقسيم'}
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium">
              {isEn ? 'Bilingual Split' : 'توزيع اللغات'}
            </CardTitle>
            <Languages className="h-4 w-4 text-primary" />
          </CardHeader>
          <CardContent>
            <div className="flex flex-col space-y-1">
              {stats?.languages.map(l => (
                <div key={l.language} className="flex justify-between items-center text-sm">
                  <span className="font-medium">{l.language.toUpperCase()}</span>
                  <span>{l.documents}</span>
                </div>
              ))}
              {(!stats?.languages || stats.languages.length === 0) && (
                <span className="text-sm text-muted-foreground">N/A</span>
              )}
            </div>
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Card className="col-span-1">
          <CardHeader>
            <CardTitle>{isEn ? 'System Status' : 'حالة النظام'}</CardTitle>
            <CardDescription>{isEn ? 'Current backend connectivity' : 'اتصال النظام الخلفي الحالي'}</CardDescription>
          </CardHeader>
          <CardContent>
             <div className="flex items-center space-x-4">
              <div className="h-12 w-12 rounded-full bg-green-100 flex items-center justify-center">
                <Database className="h-6 w-6 text-green-600" />
              </div>
              <div>
                <h4 className="text-sm font-semibold">Database & Vector Store</h4>
                <p className="text-sm text-muted-foreground">Connected and operational</p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

// Need to import Languages since it wasn't imported
import { Languages } from 'lucide-react';
