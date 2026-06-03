namespace Duets.Pad.State;

/// <summary>
/// Placeholder for the Canvas wire serializer.
/// </summary>
/// <remarks>
/// This type intentionally has no implementation yet. The serializer must be designed and
/// reviewed before it is wired into DuetsPad.
///
/// Expected responsibilities:
///
/// - serialize <see cref="CanvasState.Root" /> as a reduced terminal render tree;
/// - preserve the tree shape of Text, Element, and RawHtml terminal nodes;
/// - preserve Element attribute null values so the browser projection can decide how to
///   represent boolean attributes;
/// - emit deterministic output based on ElementAttributes and ElementChildren enumeration;
/// - reject terminal render node kinds that the wire schema does not explicitly support.
///
/// Non-responsibilities:
///
/// - do not perform HTML sanitization here;
/// - do not reduce high-level render nodes here;
/// - do not introduce handler, reset, or client-owned state semantics here;
/// - do not make the wire schema public API accidentally.
/// </remarks>
internal sealed class CanvasSerializer;
