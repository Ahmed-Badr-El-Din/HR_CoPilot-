import React from 'react';
import { useQuery } from '@tanstack/react-query';
import { ShieldAlert, Loader2, Search } from 'lucide-react';
import { apiClient } from '@/api/client';
import { useLanguage } from '@/hooks/useLanguage';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';

export function BiasAudit() {
  const { language } = useLanguage();
  const isEn = language === 'en';
  const [searchTerm, setSearchTerm] = React.useState('');

  const { data, isLoading } = useQuery<{ records: any[] }>({
    queryKey: ['bias-audit', searchTerm],
    queryFn: async () => {
      const url = searchTerm ? `/api/bias/audit?candidate_id=${encodeURIComponent(searchTerm)}` : '/api/bias/audit';
      const res = await apiClient.get(url);
      return res.data;
    },
  });

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-3xl font-bold tracking-tight">
          {isEn ? 'Bias Audit' : 'تدقيق التحيز'}
        </h2>
        <p className="text-muted-foreground mt-1">
          {isEn 
            ? 'Monitor the system for potential bias patterns (e.g. age, gender, nationality).' 
            : 'راقب النظام للكشف عن أنماط التحيز المحتملة (مثل العمر، الجنس، الجنسية).'}
        </p>
      </div>

      <Card>
        <CardHeader>
          <div className="flex justify-between items-center">
            <div>
              <CardTitle className="flex items-center gap-2">
                <ShieldAlert className="h-5 w-5 text-primary" />
                {isEn ? 'Audit Logs' : 'سجلات التدقيق'}
              </CardTitle>
              <CardDescription className="mt-1.5">
                {isEn ? 'Historical record of detected protected attributes.' : 'السجل التاريخي للسمات المحمية المكتشفة.'}
              </CardDescription>
            </div>
            <div className="relative w-64">
              <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
              <Input
                placeholder={isEn ? "Search Candidate ID..." : "البحث برقم المرشح..."}
                className="pl-9"
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
              />
            </div>
          </div>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex justify-center p-8"><Loader2 className="animate-spin text-primary" /></div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{isEn ? 'Candidate ID' : 'معرف المرشح'}</TableHead>
                  <TableHead>{isEn ? 'Attribute' : 'السمة'}</TableHead>
                  <TableHead>{isEn ? 'Occurrences' : 'عدد التكرار'}</TableHead>
                  <TableHead>{isEn ? 'Pattern' : 'النمط'}</TableHead>
                  <TableHead>{isEn ? 'Timestamp' : 'الوقت'}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data?.records.map((r, i) => (
                  <TableRow key={i}>
                    <TableCell className="font-mono text-xs">{r.CandidateId || 'N/A'}</TableCell>
                    <TableCell>
                      <Badge variant="outline">{r.attribute}</Badge>
                    </TableCell>
                    <TableCell>{r.Occurrences}</TableCell>
                    <TableCell className="font-mono text-xs max-w-[200px] truncate" title={r.Pattern}>
                      {r.Pattern}
                    </TableCell>
                    <TableCell className="text-muted-foreground">{new Date(r.Timestamp).toLocaleString()}</TableCell>
                  </TableRow>
                ))}
                {(!data?.records || data.records.length === 0) && (
                  <TableRow>
                    <TableCell colSpan={5} className="text-center py-8 text-muted-foreground">
                      {isEn ? 'No bias records found.' : 'لا توجد سجلات تحيز.'}
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
