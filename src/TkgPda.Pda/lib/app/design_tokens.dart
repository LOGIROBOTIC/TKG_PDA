import 'package:flutter/material.dart';

/// Design tokens for the Operator client.
///
/// The client runs on two very different surfaces — a 1920x1080 Windows
/// production-line touch terminal read while standing, and a ~1024x600 Android
/// industrial PDA held in one hand — from one codebase. Before these tokens
/// existed the theme declared a reasonable scale (56px buttons, 17px labels)
/// but the busiest surface, the WP-33 container panel, overrode it down to 12px
/// labels with 4x2 padding. Sizes now come from one place so a screen cannot
/// silently undercut the scale again.
///
/// Nothing here changes business behaviour; these are presentation values only.
class PdaScale {
  const PdaScale._();

  /// Below this width the terminal layout collapses to a single column.
  static const double twoColumnBreakpoint = 1280;

  /// Below this width the compact (handheld PDA) type scale applies.
  static const double compactBreakpoint = 1024;

  /// True on the line terminal, where the operator reads from a distance and
  /// the screen can carry two columns at once.
  static bool isWide(BuildContext context) =>
      MediaQuery.sizeOf(context).width >= twoColumnBreakpoint;

  /// True on a handheld PDA, where the same content must survive one column.
  static bool isCompact(BuildContext context) =>
      MediaQuery.sizeOf(context).width < compactBreakpoint;

  // --- Type scale -------------------------------------------------------
  // Terminal values first, handheld values after the slash in the comment.
  // 16px is the floor: below it the text is not readable at arm's length on a
  // line terminal, which is what produced the 9px "disabled reason" nobody
  // could read.

  static double display(BuildContext context) => isCompact(context) ? 20 : 30;
  static double title(BuildContext context) => isCompact(context) ? 17 : 25;
  static double primaryAction(BuildContext context) =>
      isCompact(context) ? 18 : 25;
  static double secondaryAction(BuildContext context) =>
      isCompact(context) ? 16 : 21;
  static double body(BuildContext context) => isCompact(context) ? 15 : 18;
  static double caption(BuildContext context) => isCompact(context) ? 13 : 16;

  // --- Touch targets ----------------------------------------------------
  // Material's 48px minimum assumes a bare fingertip on a phone. These are
  // pressed with work gloves, often without looking directly at the screen.

  static double primaryActionHeight(BuildContext context) =>
      isCompact(context) ? 76 : 118;
  static double secondaryActionHeight(BuildContext context) =>
      isCompact(context) ? 52 : 72;

  /// Hard ceiling for the stacked status banners. They used to have none, so a
  /// bad shift (offline + error + robot alarm + active transport + ambiguous
  /// submission) could take half the screen away from the actual work.
  static double statusRegionMaxHeight(BuildContext context) =>
      MediaQuery.sizeOf(context).height * 0.32;

  /// Height of the docked container panel. The cards flex into whatever this
  /// gives them, so the panel can grow when it moves into a two-column layout
  /// without the card having to be rewritten.
  static double containerPanelHeight(BuildContext context) =>
      isCompact(context) ? 208 : 292;

  /// Above this the card can afford to stack its two actions vertically and
  /// give the available one the larger share; below it they sit side by side.
  static double stackedActionsMinHeight(BuildContext context) =>
      isCompact(context) ? 190 : 250;

  // --- Spacing ----------------------------------------------------------
  static double gutter(BuildContext context) => isCompact(context) ? 10 : 16;
  static double cardPadding(BuildContext context) =>
      isCompact(context) ? 13 : 20;
}

/// Colour for a transport presentation state.
///
/// These are the states the backend projection exposes to the operator, not
/// [TransportRequestStatus]. Severity has to be legible at a glance from across
/// the line, so every state maps to one of five meanings — previously they all
/// rendered in the same neutral grey chip and "Hazır" looked like "Hata".
class PdaStatusColors {
  const PdaStatusColors._();

  static const Color done = Color(0xFF16835D);
  static const Color running = Color(0xFF2F6FED);
  static const Color waiting = Color(0xFFC26D16);
  static const Color failed = Color(0xFFC94040);
  static const Color unknown = Color(0xFF8791A3);

  static Color of(String state) => switch (state) {
    'READY' || 'COMPLETED' => done,
    'REQUESTED' || 'QUEUED' || 'ROBOT_ASSIGNED' || 'IN_PROGRESS' => running,
    'WAITING_PREPARATION' => waiting,
    'ERROR' => failed,
    _ => unknown,
  };

  /// Tinted background for a chip carrying [of]'s colour as its foreground.
  static Color surfaceOf(String state) => of(state).withValues(alpha: 0.12);
}
