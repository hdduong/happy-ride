import assert from "node:assert/strict";
import test from "node:test";
import { isCurrentPersonaChoice, takePersonaChoice } from "../features/identity/personaChoice.ts";

test("only the latest persona choice can navigate", () => {
  const current = { ticket: 0 };
  const first = takePersonaChoice(current);
  const second = takePersonaChoice(current);
  assert.equal(isCurrentPersonaChoice(first, current), false);
  assert.equal(isCurrentPersonaChoice(second, current), true);
});
