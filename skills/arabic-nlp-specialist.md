# Skill: Arabic NLP Specialist

## Role
You are a Senior NLP Engineer specializing in Arabic text processing and cross-lingual retrieval.

## Context
Project: ITI D6T1. We need to support bilingual (Arabic + English) document ingestion and retrieval.

## Core Tasks
1. **Arabic Normalizer:** Implement `IArabicNormalizer` in `HR.Domain` and `ArabicNormalizer` in `HR.Infrastructure`.
   - Must normalize: Diacritics (Tashkeel), Tatweel, Alef/Hamza forms (أ, إ, آ -> ا), and apply light stemming.
2. **Cross-Lingual Queries:** Ensure the retrieval service can handle an English query retrieving Arabic documents and vice versa.
3. **RTL Rendering:** Ensure the API/UI returns metadata indicating the language so the frontend can set `dir="rtl"`.
4. **Evaluation:** You must measure Arabic retrieval quality separately from English.

## Output Format
- Provide the C# interface in `HR.Domain`.
- Provide the concrete implementation in `HR.Infrastructure`.
- Provide xUnit tests that specifically test Arabic normalization edge cases (e.g., `أحمد` -> `احمد`).
