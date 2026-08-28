import { useState } from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Input } from "@/components/ui";

describe("Input", () => {
  it("fills the placeholder when tab is pressed on an empty opt-in field", () => {
    render(<Input acceptPlaceholderOnTab placeholder="Control engine credentials" aria-label="Store name" />);
    const input = screen.getByLabelText("Store name");

    fireEvent.keyDown(input, { key: "Tab" });

    expect(input).toHaveValue("Control engine credentials");
  });

  it("does not fill when the field already has a value", () => {
    render(<Input acceptPlaceholderOnTab placeholder="Control engine credentials" defaultValue="Custom value" aria-label="Store name" />);
    const input = screen.getByLabelText("Store name");

    fireEvent.keyDown(input, { key: "Tab" });

    expect(input).toHaveValue("Custom value");
  });

  it("does not fill when opt-in is not enabled", () => {
    render(<Input placeholder="Control engine credentials" aria-label="Store name" />);
    const input = screen.getByLabelText("Store name");

    fireEvent.keyDown(input, { key: "Tab" });

    expect(input).toHaveValue("");
  });

  it("updates controlled values when accepting placeholder on tab", () => {
    function ControlledInputHarness() {
      const [value, setValue] = useState("");
      return (
        <>
          <Input
            value={value}
            onChange={(event) => setValue(event.target.value)}
            acceptPlaceholderOnTab
            placeholder="Control engine credentials"
            aria-label="Store name"
          />
          <output aria-label="Current value">{value}</output>
        </>
      );
    }

    render(<ControlledInputHarness />);
    const input = screen.getByLabelText("Store name");

    fireEvent.keyDown(input, { key: "Tab" });

    expect(input).toHaveValue("Control engine credentials");
    expect(screen.getByLabelText("Current value")).toHaveTextContent("Control engine credentials");
  });
});
