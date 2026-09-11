import React, { createContext, useContext, useEffect, useState } from 'react';
import type { User, AuthResponse } from '@/types/api';
import { apiClient } from '@/api/client';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';

interface AuthContextType {
  user: User | null;
  isLoading: boolean;
  login: (data: AuthResponse) => void;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const queryClient = useQueryClient();
  const [token, setToken] = useState<string | null>(localStorage.getItem('access_token'));

  // Automatically fetch user if token exists
  const { data: user, isLoading } = useQuery<User>({
    queryKey: ['me'],
    queryFn: async () => {
      const res = await apiClient.get('/api/auth/me');
      return res.data;
    },
    enabled: !!token,
    retry: false,
  });

  // If token fetch fails (401), token is removed in interceptor, but we sync state here
  useEffect(() => {
    if (!localStorage.getItem('access_token') && token) {
      setToken(null);
      queryClient.setQueryData(['me'], null);
    }
  }, [token, queryClient]);

  const login = (data: AuthResponse) => {
    localStorage.setItem('access_token', data.access_token);
    setToken(data.access_token);
    queryClient.invalidateQueries({ queryKey: ['me'] });
  };

  const logout = () => {
    localStorage.removeItem('access_token');
    setToken(null);
    queryClient.setQueryData(['me'], null);
  };

  const isActuallyLoading = token ? isLoading : false;

  return (
    <AuthContext.Provider value={{ user: user || null, isLoading: isActuallyLoading, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
