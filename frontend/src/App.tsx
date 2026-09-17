import React from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AuthProvider } from './features/auth/AuthContext';
import { Login } from './features/auth/Login';
import { ProtectedRoute } from './features/auth/ProtectedRoute';
import { AppShell } from './components/layout/AppShell';

// Lazy load features
const Dashboard = React.lazy(() => import('./features/dashboard/Dashboard').then(m => ({ default: m.Dashboard })));
const Documents = React.lazy(() => import('./features/documents/Documents').then(m => ({ default: m.Documents })));
const Copilot = React.lazy(() => import('./features/copilot/Copilot').then(m => ({ default: m.Copilot })));
const Screening = React.lazy(() => import('./features/screening/Screening').then(m => ({ default: m.Screening })));
const Runs = React.lazy(() => import('./features/runs/Runs').then(m => ({ default: m.Runs })));
const RunTrace = React.lazy(() => import('./features/runs/RunTrace').then(m => ({ default: m.RunTrace })));
const Approvals = React.lazy(() => import('./features/approvals/Approvals').then(m => ({ default: m.Approvals })));
const BiasAudit = React.lazy(() => import('./features/audit/BiasAudit').then(m => ({ default: m.BiasAudit })));
const Observability = React.lazy(() => import('./features/audit/Observability').then(m => ({ default: m.Observability })));

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<Login />} />
            
            <Route path="/app" element={<ProtectedRoute />}>
              <Route element={<AppShell />}>
                <Route index element={
                  <React.Suspense fallback={<div>Loading...</div>}>
                    <Dashboard />
                  </React.Suspense>
                } />
                
                <Route path="copilot" element={
                  <React.Suspense fallback={<div>Loading...</div>}>
                    <Copilot />
                  </React.Suspense>
                } />
                
                <Route path="documents" element={
                  <React.Suspense fallback={<div>Loading...</div>}>
                    <Documents />
                  </React.Suspense>
                } />
                
                <Route path="screening" element={
                  <ProtectedRoute allowedRoles={['Admin', 'HiringManager']} />
                }>
                  <Route index element={
                    <React.Suspense fallback={<div>Loading...</div>}>
                      <Screening />
                    </React.Suspense>
                  } />
                </Route>

                <Route path="approvals" element={
                  <ProtectedRoute allowedRoles={['Admin', 'HiringManager']} />
                }>
                  <Route index element={
                    <React.Suspense fallback={<div>Loading...</div>}>
                      <Approvals />
                    </React.Suspense>
                  } />
                </Route>
                
                <Route path="runs" element={
                  <React.Suspense fallback={<div>Loading...</div>}>
                    <Runs />
                  </React.Suspense>
                } />
                
                <Route path="runs/:id" element={
                  <React.Suspense fallback={<div>Loading...</div>}>
                    <RunTrace />
                  </React.Suspense>
                } />

                <Route path="audit" element={
                  <ProtectedRoute allowedRoles={['Admin', 'Auditor']} />
                }>
                  <Route index element={
                    <React.Suspense fallback={<div>Loading...</div>}>
                      <BiasAudit />
                    </React.Suspense>
                  } />
                </Route>

                <Route path="observability" element={
                  <ProtectedRoute allowedRoles={['Admin', 'Auditor']} />
                }>
                  <Route index element={
                    <React.Suspense fallback={<div>Loading...</div>}>
                      <Observability />
                    </React.Suspense>
                  } />
                </Route>
              </Route>
            </Route>

            <Route path="*" element={<Navigate to="/app" replace />} />
          </Routes>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  );
}
