# [PROJECT NAME] Development Guidelines

Auto-generated from all feature plans. Last updated: [DATE]

## Active Technologies

[EXTRACTED FROM ALL PLAN.MD FILES]

## Project Structure

```text
[ACTUAL STRUCTURE FROM PLANS]
```

## Commands

[ONLY COMMANDS FOR ACTIVE TECHNOLOGIES]

## Code Style

[LANGUAGE-SPECIFIC, ONLY FOR LANGUAGES IN USE]

## Recent Changes

[LAST 3 FEATURES AND WHAT THEY ADDED]

<!-- MANUAL ADDITIONS START -->
## Workroom model fallback

- Root agent: use Sol 5.6 High. If unavailable, use the closest available Sol/Terra model at high reasoning, then the closest available frontier model; report the exact fallback.
- Delegates: use Luna Extra High. If unavailable, use Luna High, then the closest available model at high reasoning; report the exact fallback.
- Treat model unavailability separately from delegation timeouts or failures. After a bounded wait, the root agent continues, owns integration and QA, and reports when no delegated result was available for review.
<!-- MANUAL ADDITIONS END -->
