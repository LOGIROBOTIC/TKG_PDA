import 'package:flutter/material.dart';

import '../../data/models/pda_models.dart';
import '../../shared/presentation.dart';

class LayoutCanvas extends StatelessWidget {
  const LayoutCanvas({
    required this.slots,
    required this.highlightedSlotId,
    super.key,
  });

  final List<LayoutSlot> slots;
  final String? highlightedSlotId;

  @override
  Widget build(BuildContext context) {
    final positioned = slots.where(_hasGeometry).toList(growable: false);
    final unpositioned = slots.where((slot) => !_hasGeometry(slot)).toList();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Row(
          children: [
            Icon(Icons.info_outline, size: 18),
            SizedBox(width: 8),
            Expanded(
              child: Text(
                'Ekran yerleşimidir; robot veya SEER haritası değildir.',
                style: TextStyle(fontWeight: FontWeight.w700),
              ),
            ),
          ],
        ),
        const SizedBox(height: 10),
        LayoutBuilder(
          builder: (context, outerConstraints) => AspectRatio(
            // Narrower screens (phones/PDAs) get a taller canvas so each
            // slot keeps a usable touch target in absolute pixels, without
            // changing the slots' relative x/y/width/height proportions.
            aspectRatio: outerConstraints.maxWidth < 480 ? 1.0 : 1.55,
            child: DecoratedBox(
              decoration: BoxDecoration(
                color: const Color(0xFFE2E8F0),
                border: Border.all(color: const Color(0xFF64748B), width: 2),
                borderRadius: BorderRadius.circular(8),
              ),
              child: InteractiveViewer(
                minScale: 0.8,
                maxScale: 3,
                boundaryMargin: const EdgeInsets.all(80),
                child: LayoutBuilder(
                  builder: (context, constraints) => Stack(
                    clipBehavior: Clip.hardEdge,
                    children: [
                      for (final slot in positioned)
                        _positionedSlot(constraints, slot),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
        if (unpositioned.isNotEmpty) ...[
          const SizedBox(height: 12),
          const Text(
            'Yerleşim kanıtı olmayan konumlar',
            style: TextStyle(fontWeight: FontWeight.w800),
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [for (final slot in unpositioned) _unpositioned(slot)],
          ),
        ],
      ],
    );
  }

  Widget _positionedSlot(BoxConstraints constraints, LayoutSlot slot) {
    final x = slot.x!.clamp(0.0, 1.0);
    final y = slot.y!.clamp(0.0, 1.0);
    final width = slot.width!.clamp(0.0, 1.0 - x);
    final height = slot.height!.clamp(0.0, 1.0 - y);
    return Positioned(
      left: constraints.maxWidth * x,
      top: constraints.maxHeight * y,
      width: constraints.maxWidth * width,
      height: constraints.maxHeight * height,
      child: _slotSurface(slot),
    );
  }

  Widget _unpositioned(LayoutSlot slot) =>
      SizedBox(height: 52, child: _slotSurface(slot, compact: true));

  Widget _slotSurface(LayoutSlot slot, {bool compact = false}) {
    final highlighted = highlightedSlotId == slot.id;
    final machine = slot.isMachineRegion;
    final background = machine
        ? const Color(0xFF334155)
        : highlighted
        ? const Color(0xFFF59E0B)
        : _slotColor(slot);
    final foreground = machine || slot.occupancyState == 'OCCUPIED'
        ? Colors.white
        : const Color(0xFF0F172A);
    final child = Container(
      key: Key('slot-${slot.id}'),
      decoration: BoxDecoration(
        color: background,
        border: Border.all(
          color: highlighted ? const Color(0xFF7C2D12) : Colors.white70,
          width: highlighted ? 3 : 1,
        ),
        borderRadius: BorderRadius.circular(5),
      ),
      padding: EdgeInsets.all(compact ? 8 : 4),
      alignment: Alignment.center,
      child: FittedBox(
        fit: BoxFit.scaleDown,
        child: Text(
          machine
              ? 'Makine Alanı'
              : operatorLocationLabel(slot.displayName, slot.code),
          textAlign: TextAlign.center,
          style: TextStyle(
            color: foreground,
            fontSize: 13,
            fontWeight: FontWeight.w800,
          ),
        ),
      ),
    );
    if (machine) {
      return Semantics(
        label: 'Makine Alanı; taşıma dışı',
        button: false,
        child: child,
      );
    }
    return Semantics(
      label:
          '${operatorLocationLabel(slot.displayName, slot.code)}; '
          '${slot.occupancyState}; ${slot.loadState}'
          '${highlighted ? '; otomatik seçilen konum' : ''}',
      button: false,
      child: highlighted
          ? Stack(
              clipBehavior: Clip.none,
              children: [
                child,
                const Positioned(
                  top: -6,
                  right: -6,
                  child: _AutoResolvedBadge(),
                ),
              ],
            )
          : child,
    );
  }

  static Color _slotColor(LayoutSlot slot) => switch (slot.occupancyState) {
    'VACANT' => const Color(0xFFBBF7D0),
    'OCCUPIED' when slot.loadState == 'FULL' => const Color(0xFFB91C1C),
    'OCCUPIED' => const Color(0xFF1D4ED8),
    _ => const Color(0xFFFDE68A),
  };

  static bool _hasGeometry(LayoutSlot slot) =>
      slot.x != null &&
      slot.y != null &&
      slot.width != null &&
      slot.height != null &&
      slot.width! > 0 &&
      slot.height! > 0;
}

/// Passive status marker for the machine-side slot resolved by the backend.
class _AutoResolvedBadge extends StatelessWidget {
  const _AutoResolvedBadge();

  @override
  Widget build(BuildContext context) => Container(
    key: const Key('slot-auto-highlight-badge'),
    width: 20,
    height: 20,
    decoration: const BoxDecoration(
      color: Color(0xFF7C2D12),
      shape: BoxShape.circle,
      border: Border.fromBorderSide(BorderSide(color: Colors.white, width: 2)),
    ),
    child: const Icon(Icons.check, size: 13, color: Colors.white),
  );
}
