import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { format } from 'date-fns';
import { apiClient } from '@/api/client';
import type { DocumentListResponse, Document } from '@/types/api';
import { useLanguage } from '@/hooks/useLanguage';
import { useAuth } from '@/features/auth/AuthContext';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { FileUp, Loader2, Eye, FileText, Trash2 } from 'lucide-react';

export function Documents() {
  const { language } = useLanguage();
  const { user } = useAuth();
  const isEn = language === 'en';
  const queryClient = useQueryClient();
  
  const [file, setFile] = useState<File | null>(null);

  const { data, isLoading } = useQuery<DocumentListResponse>({
    queryKey: ['documents'],
    queryFn: async () => {
      const res = await apiClient.get('/api/documents');
      return res.data;
    },
    refetchInterval: 15000,
  });

  const uploadMutation = useMutation({
    mutationFn: async (uploadFile: File) => {
      const formData = new FormData();
      formData.append('file', uploadFile);
      const res = await apiClient.post('/api/documents/ingest', formData, {
        headers: { 'Content-Type': 'multipart/form-data' },
      });
      return res.data;
    },
    onSuccess: () => {
      setFile(null);
      queryClient.invalidateQueries({ queryKey: ['documents'] });
    },
  });

  const deleteMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiClient.delete(`/api/documents/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['documents'] });
    },
  });

  const canUpload = user?.roles.includes('HiringManager') || user?.roles.includes('Admin');

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      setFile(e.target.files[0]);
    }
  };

  const handleUpload = () => {
    if (file) {
      uploadMutation.mutate(file);
    }
  };

  const handleDelete = (id: string, title: string) => {
    if (window.confirm(isEn ? `Are you sure you want to delete ${title}?` : `هل أنت متأكد من حذف ${title}؟`)) {
      deleteMutation.mutate(id);
    }
  };

  const getStatusBadge = (status: string) => {
    switch (status) {
      case 'Ready': return <Badge variant="success">{isEn ? 'Ready' : 'جاهز'}</Badge>;
      case 'Failed': return <Badge variant="destructive">{isEn ? 'Failed' : 'فشل'}</Badge>;
      case 'Processing': return <Badge variant="warning">{isEn ? 'Processing' : 'قيد المعالجة'}</Badge>;
      default: return <Badge variant="secondary">{status}</Badge>;
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <div>
          <h2 className="text-3xl font-bold tracking-tight">
            {isEn ? 'Documents' : 'المستندات'}
          </h2>
          <p className="text-muted-foreground mt-1">
            {isEn 
              ? 'Manage the knowledge base and candidate resumes.' 
              : 'إدارة قاعدة المعرفة والسير الذاتية للمرشحين.'}
          </p>
        </div>
        
        {canUpload && (
          <div className="flex items-center gap-2 bg-card border rounded-md p-2">
            <Input 
              type="file" 
              className="w-[250px] cursor-pointer" 
              onChange={handleFileChange}
              accept=".pdf,.txt,.docx"
              disabled={uploadMutation.isPending}
            />
            <Button 
              onClick={handleUpload} 
              disabled={!file || uploadMutation.isPending}
              size="sm"
            >
              {uploadMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin me-2" /> : <FileUp className="h-4 w-4 me-2" />}
              {isEn ? 'Upload' : 'رفع'}
            </Button>
          </div>
        )}
      </div>

      <Card>
        <CardHeader>
          <CardTitle>{isEn ? 'Document Corpus' : 'المستندات'}</CardTitle>
          <CardDescription>
            {isEn ? `Total of ${data?.total || 0} documents indexed.` : `إجمالي المستندات المفهرسة: ${data?.total || 0}.`}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="flex justify-center p-8">
              <Loader2 className="h-8 w-8 animate-spin text-primary" />
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{isEn ? 'Title' : 'العنوان'}</TableHead>
                  <TableHead>{isEn ? 'Status' : 'الحالة'}</TableHead>
                  <TableHead>{isEn ? 'Language' : 'اللغة'}</TableHead>
                  <TableHead>{isEn ? 'Pages' : 'الصفحات'}</TableHead>
                  <TableHead>{isEn ? 'Uploaded' : 'تاريخ الرفع'}</TableHead>
                  <TableHead className="w-[100px]"></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data?.documents.map((doc: Document) => (
                  <TableRow key={doc.id}>
                    <TableCell className="font-medium">
                      <div className="flex items-center">
                        <FileText className="h-4 w-4 text-muted-foreground me-2" />
                        <span className="truncate max-w-[250px]" title={doc.title}>
                          {doc.title}
                        </span>
                      </div>
                    </TableCell>
                    <TableCell>{getStatusBadge(doc.status)}</TableCell>
                    <TableCell>
                      <Badge variant="outline">{doc.language.toUpperCase()}</Badge>
                    </TableCell>
                    <TableCell>{doc.page_count}</TableCell>
                    <TableCell className="text-muted-foreground">
                      {format(new Date(doc.created_at), 'MMM d, yyyy')}
                    </TableCell>
                    <TableCell>
                      <div className="flex gap-2 justify-end">
                        <Button variant="ghost" size="icon" title={isEn ? "View details" : "عرض التفاصيل"}>
                          <Eye className="h-4 w-4" />
                        </Button>
                        <Button 
                          variant="outline" 
                          size="icon" 
                          title={isEn ? "Delete document" : "حذف المستند"}
                          onClick={() => handleDelete(doc.id, doc.title)}
                          disabled={deleteMutation.isPending && deleteMutation.variables === doc.id}
                          className="border-red-200 bg-red-50 text-red-600 hover:bg-red-100 hover:text-red-700 hover:border-red-300 dark:border-red-900/30 dark:bg-red-900/10 dark:text-red-400 dark:hover:bg-red-900/20 dark:hover:border-red-800 transition-all shadow-sm"
                        >
                          {deleteMutation.isPending && deleteMutation.variables === doc.id ? (
                            <Loader2 className="h-4 w-4 animate-spin" />
                          ) : (
                            <Trash2 className="h-4 w-4" />
                          )}
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
                {!data?.documents?.length && (
                  <TableRow>
                    <TableCell colSpan={6} className="text-center text-muted-foreground py-8">
                      {isEn ? 'No documents found.' : 'لم يتم العثور على مستندات.'}
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

