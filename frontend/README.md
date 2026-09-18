# HR Copilot - Frontend

This is the React frontend for the HR Copilot (D6T1) platform, an Agentic RAG system for HR Talent Screening.

## Architecture & Tech Stack

- **Framework**: React 18 with TypeScript, bundled by Vite.
- **Routing**: React Router v6.
- **State & Data Fetching**: TanStack Query (React Query) for server-state caching, Axios for the API client.
- **Forms**: React Hook Form with Zod for validation.
- **Styling**: Tailwind CSS with custom primitives (shadcn/ui style), Lucide icons.
- **Events**: Native `EventSource` for Server-Sent Events (SSE).

## Features

1. **Authentication**: JWT-based login using the `.NET Identity` backend.
2. **Dashboard**: High-level observability of the RAG document corpus.
3. **Documents**: Upload resumes/documents to the ingestion pipeline.
4. **Copilot**: Grounded Q&A chat interface with live citation tracking via SSE.
5. **Screening Workflow**: Launch complex agentic workflows to screen candidates against a rubric.
6. **Live Runs**: Real-time visual trace of agent actions (`Agent`, `Tool`, `Metrics`) via SSE.
7. **Approvals Queue**: Human-in-the-loop gate to approve, reject, or edit AI-generated drafts.
8. **Bias Audit**: Real-time observability of potential discriminatory behaviors flagged by the AI.
9. **RTL & Bilingual Support**: First-class Arabic translation and right-to-left layout integration.

## Installation & Setup

1. Make sure you are using Node.js 18+.
2. Install dependencies:
   ```bash
   npm install
   ```
3. Configure the environment variables by copying `.env.example`:
   ```bash
   cp .env.example .env
   ```
4. Update `VITE_API_BASE_URL` in `.env` to point to your running `.NET` API (default is `http://localhost:5221`).

## Running the Application

```bash
npm run dev
```

The application will start on `http://localhost:5173`.

## Authentication Flow

The frontend relies on the `POST /api/auth/login` endpoint. Upon success, a JWT is stored in `localStorage`.
An Axios interceptor automatically attaches this `Bearer` token to all outbound REST requests.
For SSE (`EventSource`), the token is appended as a query string parameter (`?access_token=...`) since the browser's native `EventSource` API does not support custom headers.

## Known Limitations

- **File Uploads**: The document ingestion endpoint has a configured maximum file size (managed by the backend).
- **SSE Fallbacks**: If the connection drops, you may need to refresh to see historical events until a robust reconnect loop is added to the EventSource wrapper.
