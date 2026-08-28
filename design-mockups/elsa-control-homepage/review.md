# Elsa Control homepage design review

## What is already working

- The dark technical visual language fits a developer infrastructure product.
- The amber accent is distinctive, readable, and consistently associated with action.
- The site demonstrates genuine product depth rather than relying on vague enterprise language.
- The Studio → Elsa Control → Runtime boundary is explained accurately.
- Tables, manifests, deployment steps, and platform metrics create strong technical credibility.

## Highest-impact issues

### 1. The homepage behaves like a product specification

The live page is roughly 12,745 px tall at a 1280 px desktop viewport. It presents the deployment model, platform surfaces, resource taxonomy, runtime builder, package catalog, assistants, implementation status, and roadmap in one continuous narrative. That depth is valuable, but it asks a first-time visitor to understand almost the full platform before taking action.

**Recommendation:** make the homepage answer three questions in sequence:

1. What is this?
2. Why does it matter?
3. Why should I trust it?

Move resource taxonomies, role matrices, and detailed roadmap lists into dedicated product or documentation pages.

### 2. Positioning is technically accurate but not immediately concrete

“The operating system for workflow automation” is broad and category-level. The supporting paragraph carries the essential Elsa-specific positioning, but it is long and appears after the abstract claim.

**Recommendation:** lead with the customer outcome and the Elsa category:

> Ship Elsa workflows. Keep control.
>
> Turn Elsa workflows into governed releases—validated, previewed, deployed, and recorded across every environment.

### 3. The first viewport offers too many paths

The live navigation exposes eight section links plus an “Executive brief” CTA. The hero adds two more links. This creates a documentation-style index before the visitor has formed a mental model.

**Recommendation:** reduce primary navigation to Platform, How it works, Trust, and Roadmap, with one persistent “Request access” CTA.

### 4. Visual hierarchy becomes uniform below the hero

Many sections use the same dark background, compact eyebrow, large heading, paragraph, and bordered technical panel. Individual components are thoughtfully made, but the repeated section rhythm reduces contrast and makes the page feel longer.

**Recommendation:** alternate narrative modes:

- a high-emotion hero;
- a light editorial problem section;
- one signature deployment journey;
- a compact capability grid;
- a trust ledger;
- a short roadmap and conversion close.

### 5. Product detail is stronger than buyer proof

The site shows architecture and roadmap depth, but it has little evidence about adoption, ownership, security posture, support, or who should engage now. The existing platform metrics look convincing, but their context is unclear.

**Recommendation:** label metrics carefully, add concrete trust statements, explain deployment options, and use one buyer-oriented CTA. Add customer or design-partner proof when available.

### 6. Production metadata still points to a previous identity

The live page's canonical URL, Open Graph URL, and structured-data organization/site URLs point to `elsaplatform.com`; the X/Twitter site metadata is `@Lovable`. These details are invisible in the layout but materially weaken search, sharing, and launch credibility.

**Recommendation:** update canonical, Open Graph, schema.org, and social metadata to the official Elsa Control domain and accounts before promoting the page.

## Mockup direction

The redesign uses **calm operational precision** rather than a generic cyber aesthetic.

- **Domain vocabulary:** desired state, artifacts, promotion, reconciliation, runtime boundaries, provenance.
- **Color world:** blueprint navy, signal amber, terminal green, steel, paper, and fog.
- **Signature element:** a deployment signal rail that follows one immutable artifact from build to runtime.
- **Typography:** Manrope for confident editorial hierarchy and IBM Plex Mono for operational metadata.
- **Depth:** quiet surface shifts and low-opacity borders, with amber reserved for action and the active signal.
- **Content shape:** six major sections instead of a full product brief.

The mockup is a standalone responsive prototype with an interactive deployment rail, responsive navigation, motion that respects reduced-motion settings, and desktop/mobile layouts.
