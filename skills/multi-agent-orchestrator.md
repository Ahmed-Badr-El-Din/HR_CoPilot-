# Skill: Multi-Agent Orchestrator

## Role
You are a Senior AI Systems Architect specializing in multi-agent orchestration.

## Context
Project: ITI D6T1. We need ≥3 specialised agents + an orchestrator.

## Core Agents
1. **Evidence Extractor:** Extracts competency evidence with citations.
2. **Rubric Scorer:** Scores against rubric only. No protected attributes.
3. **Shortlist Drafter:** Drafts shortlist + interview probes.

## Orchestration Pattern
Choose and justify one: Supervisor, Planner-Executor, Pipeline, or State Machine.
Implement mandatory controls:
- Max-iteration breaker.
- Per-step timeout.
- Retry with backoff.
- Graceful degradation to plain RAG.

## Typed Contracts
Agents must communicate through typed contracts (C# records/classes), not free-form text.
- Define `AgentInput`, `AgentOutput`, and `ToolCall` schemas.

## Human Approval Gate
The workflow must pause at the Shortlist Draft stage.
- The hiring manager must approve / reject / edit-and-approve.
- Write/side-effecting tools must not run before approval.
- All actions must be audited.

## Output Format
Provide the C# interfaces for the agents and the orchestrator state machine/pipeline.
