import React from 'react';
import { NavLink } from 'react-router-dom';
import { 
  LayoutDashboard, 
  MessageSquare, 
  Files, 
  Users, 
  CheckSquare, 
  Activity, 
  ShieldAlert, 
  BarChart3,
  LogOut,
  Languages
} from 'lucide-react';
import { useAuth } from '@/features/auth/AuthContext';
import { useLanguage } from '@/hooks/useLanguage';
import { cn } from '@/lib/utils';

export function Sidebar() {
  const { user, logout } = useAuth();
  const { language, setLanguage } = useLanguage();

  const toggleLanguage = () => {
    setLanguage(language === 'en' ? 'ar' : 'en');
  };

  const navItems = [
    { name: language === 'en' ? 'Overview' : 'نظرة عامة', path: '/app', icon: LayoutDashboard, exact: true },
    { name: language === 'en' ? 'Copilot' : 'مساعد الذكاء الاصطناعي', path: '/app/copilot', icon: MessageSquare },
    { name: language === 'en' ? 'Documents' : 'المستندات', path: '/app/documents', icon: Files },
    { name: language === 'en' ? 'Screen Candidates' : 'فحص المرشحين', path: '/app/screening', icon: Users, roles: ['Admin', 'HiringManager'] },
    { name: language === 'en' ? 'Approvals' : 'الموافقات', path: '/app/approvals', icon: CheckSquare, roles: ['Admin', 'HiringManager'] },
    { name: language === 'en' ? 'Runs' : 'عمليات التشغيل', path: '/app/runs', icon: Activity },
    { name: language === 'en' ? 'Bias Audit' : 'تدقيق التحيز', path: '/app/audit', icon: ShieldAlert, roles: ['Admin', 'Auditor'] },
    { name: language === 'en' ? 'Observability' : 'المراقبة', path: '/app/observability', icon: BarChart3, roles: ['Admin', 'Auditor'] },
  ];

  const visibleItems = navItems.filter(
    item => !item.roles || item.roles.some(r => user?.roles.includes(r))
  );

  return (
    <div className="flex h-full w-64 flex-col border-e bg-card">
      <div className="flex h-14 items-center border-b px-6">
        <ShieldAlert className="h-6 w-6 text-primary me-2" />
        <span className="font-semibold tracking-tight">HR Copilot</span>
      </div>

      <div className="flex-1 overflow-y-auto py-4">
        <nav className="space-y-1 px-3">
          {visibleItems.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              end={item.exact}
              className={({ isActive }) =>
                cn(
                  "flex items-center rounded-md px-3 py-2 text-sm font-medium transition-colors",
                  isActive
                    ? "bg-primary/10 text-primary"
                    : "text-muted-foreground hover:bg-muted hover:text-foreground"
                )
              }
            >
              <item.icon className="h-4 w-4 me-3" />
              {item.name}
            </NavLink>
          ))}
        </nav>
      </div>

      <div className="border-t p-4 space-y-4">
        <div className="flex items-center justify-between">
          <div className="flex flex-col truncate">
            <span className="text-sm font-medium truncate">{user?.email}</span>
            <span className="text-xs text-muted-foreground truncate">{user?.roles.join(', ')}</span>
          </div>
        </div>
        
        <div className="flex items-center gap-2">
          <button
            onClick={toggleLanguage}
            className="flex flex-1 items-center justify-center rounded-md border p-2 text-xs font-medium hover:bg-muted transition-colors"
          >
            <Languages className="h-4 w-4 me-2" />
            {language === 'en' ? 'العربية' : 'English'}
          </button>
          
          <button
            onClick={logout}
            className="flex items-center justify-center rounded-md border p-2 text-muted-foreground hover:bg-destructive hover:text-destructive-foreground transition-colors"
            title={language === 'en' ? 'Logout' : 'تسجيل الخروج'}
          >
            <LogOut className="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  );
}
