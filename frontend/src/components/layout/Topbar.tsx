import React from 'react';
import { useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/api/client';
import { useLanguage } from '@/hooks/useLanguage';

export function Topbar() {
  const location = useLocation();
  const { language } = useLanguage();

  const { data: health } = useQuery({
    queryKey: ['health'],
    queryFn: async () => {
      const res = await apiClient.get('/healthz');
      return res.data;
    },
    refetchInterval: 30000,
  });

  const getPageTitle = () => {
    const path = location.pathname;
    if (path === '/app') return language === 'en' ? 'Overview' : 'نظرة عامة';
    if (path.startsWith('/app/copilot')) return language === 'en' ? 'Copilot' : 'مساعد الذكاء الاصطناعي';
    if (path.startsWith('/app/documents')) return language === 'en' ? 'Documents' : 'المستندات';
    if (path.startsWith('/app/screening')) return language === 'en' ? 'Screen Candidates' : 'فحص المرشحين';
    if (path.startsWith('/app/approvals')) return language === 'en' ? 'Approvals' : 'الموافقات';
    if (path.startsWith('/app/runs')) return language === 'en' ? 'Runs' : 'عمليات التشغيل';
    if (path.startsWith('/app/audit')) return language === 'en' ? 'Bias Audit' : 'تدقيق التحيز';
    if (path.startsWith('/app/observability')) return language === 'en' ? 'Observability' : 'المراقبة';
    return '';
  };

  return (
    <header className="flex h-14 items-center justify-between border-b bg-card px-6">
      <h1 className="text-lg font-semibold">{getPageTitle()}</h1>
      
      <div className="flex items-center gap-4">
        <div className="flex items-center gap-2 text-sm">
          <span className="text-muted-foreground">System:</span>
          <div className="flex items-center gap-1.5">
            <div className={`h-2 w-2 rounded-full ${health?.status === 'ok' ? 'bg-green-500' : 'bg-yellow-500'}`} />
            <span className="font-medium">{health?.status === 'ok' ? 'Healthy' : 'Checking...'}</span>
          </div>
        </div>
      </div>
    </header>
  );
}
