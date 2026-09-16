import React from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { format } from 'date-fns';
import { Play, Loader2, Search, ArrowRight } from 'lucide-react';
import { apiClient } from '@/api/client';
import type { Run } from '@/types/api';
import { useLanguage } from '@/hooks/useLanguage';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';

export function Runs() {
  const navigate = useNavigate();
  const { language } = useLanguage();
  const isEn = language === 'en';

  const { data, isLoading } = useQuery<{ runs: Run[] }>({
    queryKey: ['runs'],
    queryFn: async () => {
      const res = await apiClient.get('/api/runs');
      return res.data;
    },
    refetchInterval: 10000,
  });

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'Running': return <Badge variant="warning">{isEn ? 'Running' : 'قيد التشغيل'}</Badge>;
      case 'AwaitingApproval': return <Badge variant="secondary">{isEn ? 'Awaiting Approval' : 'بانتظار الموافقة'}</Badge>;
      case 'Completed': return <Badge variant="success">{isEn ? 'Completed' : 'مكتمل'}</Badge>;
      case 'Failed': return <Badge variant="destructive">{isEn ? 'Failed' : 'فشل'}</Badge>;
      default: return <Badge variant="outline">{status}</Badge>;
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-3xl font-bold tracking-tight">
          {isEn ? 'Runs' : 'عمليات التشغيل'}
        </h2>
        <p className="text-muted-foreground mt-1">
          {isEn 
            ? 'Monitor active and historical agent workflows.' 
            : 'مراقبة مسارات عمل الوكلاء النشطة والسابقة.'}
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>{isEn ? 'Workflow Executions' : 'تنفيذ مسارات العمل'}</CardTitle>
          <CardDescription>
            {isEn ? 'Recent screening and general workflow runs.' : 'أحدث عمليات فحص المرشحين ومسارات العمل العامة.'}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex justify-center p-8"><Loader2 className="animate-spin text-primary h-8 w-8" /></div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{isEn ? 'Run ID' : 'معرف التشغيل'}</TableHead>
                  <TableHead>{isEn ? 'Type' : 'النوع'}</TableHead>
                  <TableHead>{isEn ? 'Status' : 'الحالة'}</TableHead>
                  <TableHead>{isEn ? 'Degraded' : 'منخفض الأداء'}</TableHead>
                  <TableHead>{isEn ? 'Started' : 'تاريخ البدء'}</TableHead>
                  <TableHead className="w-[100px]"></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data?.runs.map((run) => (
                  <TableRow key={run.id} className="cursor-pointer hover:bg-muted/50" onClick={() => navigate(`/app/runs/${run.id}`)}>
                    <TableCell className="font-medium font-mono text-xs text-muted-foreground">
                      {run.id.split('-')[0]}...
                    </TableCell>
                    <TableCell>
                      <div className="flex items-center">
                        <Play className="h-4 w-4 me-2 text-primary" />
                        {run.kind}
                      </div>
                    </TableCell>
                    <TableCell>{getStatusBadge(run.status)}</TableCell>
                    <TableCell>
                      {run.degraded ? <Badge variant="destructive">Yes</Badge> : <Badge variant="outline">No</Badge>}
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      {format(new Date(run.created_at), 'MMM d, h:mm a')}
                    </TableCell>
                    <TableCell>
                      <Button variant="ghost" size="icon">
                        <ArrowRight className="h-4 w-4" />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
                {!data?.runs?.length && (
                  <TableRow>
                    <TableCell colSpan={6} className="text-center py-8 text-muted-foreground">
                      {isEn ? 'No runs found.' : 'لم يتم العثور على عمليات تشغيل.'}
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
