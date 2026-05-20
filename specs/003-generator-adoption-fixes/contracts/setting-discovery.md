# Contract: Setting Discovery For Non-Manifestable Properties

## Purpose

Define which shell-feature properties become deploy-time settings and which are
ignored because they cannot be represented in the package manifest.

## Included Deploy-Time Settings

The existing supported setting shapes remain included:

- strings, booleans, numeric values, enums
- nullable supported values
- supported date/time, duration, URI, and identifier shapes
- arrays, lists, and dictionaries whose element/value type is supported
- properties enriched by supported manifest setting hints

## Ignored Code Configuration Hooks

The following public settable properties are ignored by default:

- direct delegate-shaped properties, including action callbacks and factory
  callbacks
- properties whose element type is delegate-shaped
- dictionary properties whose value type is delegate-shaped
- nested collection or dictionary shapes that contain delegate-shaped values

Examples:

```csharp
public Action<TOptions>? Configure { get; set; }
public Action<IServiceProvider, HttpClient>? ConfigureHttpClient { get; set; }
public Func<IServiceProvider, TService>? ServiceFactory { get; set; }
public IDictionary<string, Func<IServiceProvider, ValueTask<IWorkflow>>> Factories { get; set; }
```

## Diagnostic Behavior

- Ignored code hooks and unsupported setting candidates do not appear in
  manifest settings.
- Ignored code hooks and unsupported setting candidates do not emit
  unsupported-setting errors.
- Ignored code hooks and unsupported setting candidates do not emit warnings by
  default.
- Verbose diagnostics may identify ignored code hooks and the owning feature.
- Low-importance diagnostics identify unsupported non-delegate omissions,
  including the owning feature, property name, and CLR type.

## Unsupported Settings

Non-delegate complex object settings remain unsupported unless represented by a
supported primitive, enum, nullable, array, list, or dictionary shape.
Unsupported CLR-only shapes such as `System.Type` are also unsupported.

Unsupported setting candidates are omitted from the manifest and emit
low-importance non-warning diagnostics. They must not fail default builds or
fail-on-warnings builds unless another warning or error is present.

## Acceptance Tests

- A feature with delegate hooks and one normal setting generates a manifest that
  includes only the normal setting.
- Direct delegate hooks are ignored without default warnings.
- Delegate-valued dictionaries and collections are ignored without default
  warnings.
- `System.Type` settings are omitted with low-importance diagnostics and no
  warnings or errors.
- Non-delegate unsupported object settings are omitted with low-importance
  diagnostics and no warnings or errors.
