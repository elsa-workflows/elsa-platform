import "@testing-library/jest-dom/vitest";
import { configure } from "@testing-library/react";

// findBy*/waitFor default to a 1s timeout, which these component trees exceed when vitest runs test
// files in parallel on a loaded machine — producing failures that pass on their own. Raising it here
// keeps the workaround in one place instead of at individual call sites.
configure({ asyncUtilTimeout: 5_000 });
