# Lovable implementation brief

Build a responsive Elsa Control marketing website from the attached source files.

## Non-negotiable source of truth

Use `index.html`, `styles.css`, and `script.js` exactly as supplied whenever possible. The finished website must look and behave pixel-for-pixel like the attached `preview-desktop.jpg` at 1280 × 720 and `preview-mobile.jpg` at 390 × 844.

Do not reinterpret, simplify, modernize, or restyle the design. Do not replace the composition with a generic Tailwind or shadcn landing-page template.

If Lovable's project architecture requires React/TypeScript:

- translate the supplied HTML into components without changing the rendered DOM structure or copy;
- carry over every CSS token, breakpoint, font, color, spacing value, border, animation, and responsive rule;
- preserve the deployment-stage tabs, mobile navigation, reveal motion, sticky/auto-hiding header, reduced-motion behavior, and magnetic CTA interaction;
- preserve all accessibility behavior, including the skip link, keyboard-operable tablist, Escape-to-close mobile menu, focus return, focus visibility, and section scroll offsets;
- do not introduce visual dependencies that change the appearance;
- do not invent additional content, metrics, sections, illustrations, gradients, cards, or calls to action.

## Acceptance criteria

1. Desktop visual parity with `preview-desktop.jpg` at 1280 × 720.
2. Mobile visual parity with `preview-mobile.jpg` at 390 × 844.
3. No horizontal overflow at either size.
4. Internal navigation and all deployment-stage interactions work.
5. Mobile navigation opens, closes, and closes on Escape.
6. No browser console errors.
7. `prefers-reduced-motion` is respected.

Treat the screenshots as visual regression references and the supplied HTML/CSS/JavaScript as the authoritative implementation.
