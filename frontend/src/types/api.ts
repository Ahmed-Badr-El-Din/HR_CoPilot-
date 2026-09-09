export interface User {
  sub: string;
  email: string;
  name: string | null;
  roles: string[];
}

export interface AuthResponse {
  access_token: string;
  token_type: string;
  expires_in: number;
  email: string;
  roles: string[];
}

export interface Document {
  id: string;
  title: string;
  source_format: string;
  language: string;
  version: number;
  status: string;
  status_message: string | null;
  page_count: number;
  tags: string[];
  created_at: string;
}

export interface DocumentListResponse {
  total: number;
  documents: Document[];
}

export interface DocumentChunk {
  id: string;
  ordinal: number;
  section: string | null;
  page_reference: string | null;
  language: string;
  text_preview: string;
}

export interface DocumentDetail extends Document {
  source: string;
  chunk_count: number;
  chunks: DocumentChunk[];
}

export interface ChatSession {
  id: string;
  title: string;
  language: string;
  created_at: string;
  updated_at: string;
}

export interface ChatSessionMessage {
  role: string;
  content: string;
  refused: boolean;
  created_at: string;
  run_id: string | null;
  citations: any[];
}

export interface ChatSessionDetail extends ChatSession {
  messages: ChatSessionMessage[];
}

export interface Competency {
  name: string;
  description: string;
}

export interface JobRole {
  id?: string;
  title: string;
  description: string;
  competencies: Competency[];
}

export interface RubricDimension {
  id?: string;
  name: string;
  description: string;
  weight: number;
  maxScore: number;
}

export interface Rubric {
  name: string;
  dimensions: RubricDimension[];
}

export interface CandidateRef {
  id?: string;
  name?: string;
  documentId: string;
}

export interface ScreeningRequest {
  role: JobRole;
  rubric: Rubric;
  candidates: CandidateRef[];
}

export interface Run {
  id: string;
  kind: string;
  status: string;
  correlation_id: string;
  owner: string;
  degraded: boolean;
  created_at: string;
  finished_at: string | null;
}

export interface RunEvent {
  kind: string;
  agent: string | null;
  tool: string | null;
  text: string;
  payload: any;
  prompt_tokens: number;
  completion_tokens: number;
  cost_usd: number;
  created_at: string;
}

export interface RunDetail extends Run {
  request: any;
  result: any;
  error: string | null;
  started_at: string | null;
  approval_requested_at: string | null;
  events: RunEvent[];
}

export interface Approval {
  approval_id: string;
  run_id: string;
  step: string;
  role: string;
  created_at: string;
  sla_deadline: string | null;
  requested_by: string;
  payload: any;
}
