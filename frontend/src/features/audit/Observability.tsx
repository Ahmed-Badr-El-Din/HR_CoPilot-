import React from 'react';
import { useQuery } from '@tanstack/react-query';
import { BarChart3, Loader2 } from 'lucide-react';
import { apiClient } from '@/api/client';
import { useLanguage } from '@/hooks/useLanguage';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';

export function Observability() {
  const { language } = useLanguage();
  const isEn = language === 'en';

  const { data, isLoading } = useQuery<{ totals: any, records: any[] }>({
    queryKey: ['usage'],
    queryFn: async () => {
      const res = await apiClient.get('/api/usage');
      return res.data;
    },
  });

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-3xl font-bold tracking-tight">
          {isEn ? 'Observability' : 'المراقبة'}
        </h2>
        <p className="text-muted-foreground mt-1">
          {isEn 
            ? 'Track AI provider usage and cost metrics.' 
            : 'تتبع استخدام مزود الذكاء الاصطناعي ومقاييس التكلفة.'}
        </p>
      </div>

      <div className="grid gap-4 md:grid-cols-3">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium">
              {isEn ? 'Total Cost (USD)' : 'إجمالي التكلفة'}
            </CardTitle>
            <BarChart3 className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold text-primary">
              ${(data?.totals?.cost_usd || 0).toFixed(4)}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium">
              {isEn ? 'Prompt Tokens' : 'رموز الطلب'}
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono">
              {(data?.totals?.prompt_tokens || 0).toLocaleString()}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardHeader className="flex flex-row items-center justify-between pb-2">
            <CardTitle className="text-sm font-medium">
              {isEn ? 'Completion Tokens' : 'رموز الإكمال'}
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-2xl font-bold font-mono">
              {(data?.totals?.completion_tokens || 0).toLocaleString()}
            </div>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>{isEn ? 'Usage Log' : 'سجل الاستخدام'}</CardTitle>
          <CardDescription>
            {isEn ? 'Detailed record of provider calls.' : 'سجل مفصل لاتصالات مزود الذكاء الاصطناعي.'}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex justify-center p-8"><Loader2 className="animate-spin text-primary" /></div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{isEn ? 'Model' : 'النموذج'}</TableHead>
                  <TableHead>{isEn ? 'Provider' : 'المزود'}</TableHead>
                  <TableHead>{isEn ? 'Tokens (P/C)' : 'الرموز'}</TableHead>
                  <TableHead>{isEn ? 'Cost' : 'التكلفة'}</TableHead>
                  <TableHead>{isEn ? 'Timestamp' : 'الوقت'}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data?.records.map((r, i) => (
                  <TableRow key={i}>
                    <TableCell className="font-mono text-xs">{r.model}</TableCell>
                    <TableCell>{r.provider}</TableCell>
                    <TableCell className="font-mono text-xs">
                      {r.prompt_tokens} / {r.completion_tokens}
                    </TableCell>
                    <TableCell className="font-mono text-xs">${r.cost_usd.toFixed(4)}</TableCell>
                    <TableCell className="text-muted-foreground">{new Date(r.created_at).toLocaleString()}</TableCell>
                  </TableRow>
                ))}
                {(!data?.records || data.records.length === 0) && (
                  <TableRow>
                    <TableCell colSpan={5} className="text-center py-8 text-muted-foreground">
                      {isEn ? 'No usage records found.' : 'لا توجد سجلات استخدام.'}
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
