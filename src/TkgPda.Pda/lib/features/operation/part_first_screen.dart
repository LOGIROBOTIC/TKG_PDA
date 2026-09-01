import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../data/models/pda_models.dart';
import 'transport_action_panel.dart';

/// Final Operator workspace after a machine is selected. Part and compatible
/// variant resolution stay on this page; once proven, the business-operation
/// buttons appear here without a physical-slot or layout step.
class PartFirstSelector extends StatelessWidget {
  const PartFirstSelector({required this.controller, super.key});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    if (controller.partOptionsLoading && controller.partOptions.isEmpty) {
      return const Center(
        key: Key('part-options-loading'),
        child: CircularProgressIndicator(),
      );
    }
    if (controller.partOptionsError case final error?) {
      return Padding(
        padding: const EdgeInsets.all(14),
        child: _RetryState(
          key: const Key('part-options-error'),
          retryKey: const Key('part-options-retry'),
          message: error,
          onRetry: controller.reloadPartOptions,
        ),
      );
    }
    return RefreshIndicator(
      onRefresh: controller.reloadPartOptions,
      child: ListView(
        key: const Key('part-first-selector'),
        padding: const EdgeInsets.all(16),
        children: [
          const Text(
            'Parça Seçimi',
            style: TextStyle(fontSize: 20, fontWeight: FontWeight.w900),
          ),
          const SizedBox(height: 6),
          const Text('İşlem yapılacak ana parçayı seçin.'),
          const SizedBox(height: 20),
          const _SectionLabel('Ana Parçalar'),
          const SizedBox(height: 10),
          if (controller.partOptions.isEmpty)
            const Text('Bu makine için tanımlı parça bulunamadı.')
          else
            _MainPartChips(controller: controller),
          if (controller.selectedMainPart case final main?) ...[
            const SizedBox(height: 22),
            _EffectivePartCard(main: main, controller: controller),
          ],
          if (controller.selectedEffectivePart != null) ...[
            const SizedBox(height: 18),
            _CompatibleVariantCard(controller: controller),
          ],
          if (controller.transportActionsReady) ...[
            const SizedBox(height: 18),
            OperatorTransportPanel(controller: controller),
          ],
          const SizedBox(height: 40),
        ],
      ),
    );
  }
}

/// A consistent, visually subordinate section heading used to keep
/// "Ana Parçalar" and "Alternatif / İlgili Parçalar" clearly separated.
class _SectionLabel extends StatelessWidget {
  const _SectionLabel(this.text);

  final String text;

  // Deliberately not upper-cased: Dart's invariant toUpperCase() mangles the
  // Turkish dotted i ("İlgili" -> "İLGILI").
  @override
  Widget build(BuildContext context) => Text(
    text,
    style: const TextStyle(
      fontSize: 13,
      fontWeight: FontWeight.w900,
      letterSpacing: 0.4,
      color: Color(0xFF475569),
    ),
  );
}

class _MainPartChips extends StatelessWidget {
  const _MainPartChips({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) => Wrap(
      spacing: 10,
      runSpacing: 10,
      children: [
        for (final option in controller.partOptions)
          ConstrainedBox(
            constraints: BoxConstraints(maxWidth: constraints.maxWidth),
            child: _MainPartChip(option: option, controller: controller),
          ),
      ],
    ),
  );
}

/// One click selects the main part as the effective part and starts the
/// compatible-variant lookup — there is no second confirmation chip. The
/// part code leads; an unknown description is simply omitted rather than
/// repeating "Tanımlı değil" on every chip.
class _MainPartChip extends StatelessWidget {
  const _MainPartChip({required this.option, required this.controller});

  final MachineMainPartOption option;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final selected = controller.selectedMainPart?.partId == option.partId;
    final description = option.description?.trim();
    return ChoiceChip(
      key: Key('main-part-${option.partId}'),
      selected: selected,
      backgroundColor: Colors.white,
      selectedColor: const Color(0xFFFDE68A),
      side: BorderSide(
        color: selected ? const Color(0xFFB45309) : const Color(0xFFCBD5E1),
        width: selected ? 2.5 : 1,
      ),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      label: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            option.partCode,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontSize: 16,
              fontWeight: selected ? FontWeight.w900 : FontWeight.w700,
            ),
          ),
          if (description != null && description.isNotEmpty)
            Text(
              description,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w500,
                color: Color(0xFF475569),
              ),
            ),
        ],
      ),
      onSelected: controller.busy
          ? null
          : (_) => controller.selectMainPartOption(option),
    );
  }
}

class _EffectivePartCard extends StatelessWidget {
  const _EffectivePartCard({required this.main, required this.controller});

  final MachineMainPartOption main;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final effective = controller.selectedEffectivePart;
    final related = main.relatedParts
        .where((part) => part.relationType == 'RELATED')
        .toList(growable: false);
    final alt = main.relatedParts
        .where((part) => part.relationType == 'ALT_PART')
        .toList(growable: false);
    final other = main.relatedParts
        .where(
          (part) =>
              part.relationType != 'RELATED' && part.relationType != 'ALT_PART',
        )
        .toList(growable: false);

    final usingMain = effective?.isMain ?? false;
    final hasAlternatives =
        related.isNotEmpty || alt.isNotEmpty || other.isNotEmpty;

    return Card(
      key: Key('main-part-detail-${main.partId}'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // The parent main part stays visible for context even after an
            // alternative below is chosen.
            Row(
              children: [
                const Icon(Icons.inventory_2_outlined, size: 20),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    'Seçili Ana Parça · ${main.partCode}',
                    style: const TextStyle(
                      fontSize: 17,
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 6),
            Text(
              usingMain
                  ? 'Bu ana parça ile devam ediliyor.'
                  : 'Alternatif kullanılıyor: ${effective?.partCode ?? ''}',
              key: const Key('effective-part-summary'),
              style: const TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w600,
                color: Color(0xFF475569),
              ),
            ),
            if (hasAlternatives) ...[
              const Divider(height: 26),
              const _SectionLabel('Alternatif / İlgili Parçalar'),
              const SizedBox(height: 4),
              const Text(
                'Gerekirse bunlardan biriyle devam edebilirsiniz.',
                style: TextStyle(fontSize: 13, color: Color(0xFF475569)),
              ),
            ],
            if (related.isNotEmpty) ...[
              const SizedBox(height: 14),
              const Text(
                'İlgili Parçalar',
                style: TextStyle(fontWeight: FontWeight.w800),
              ),
              const SizedBox(height: 8),
              _RelatedPartChips(
                parts: related,
                keyPrefix: 'related',
                effective: effective,
                controller: controller,
              ),
            ],
            if (alt.isNotEmpty) ...[
              const SizedBox(height: 14),
              const Text(
                'Alternatif Parçalar',
                style: TextStyle(fontWeight: FontWeight.w800),
              ),
              const SizedBox(height: 8),
              _RelatedPartChips(
                parts: alt,
                keyPrefix: 'alt',
                effective: effective,
                controller: controller,
              ),
            ],
            if (other.isNotEmpty) ...[
              const SizedBox(height: 14),
              const Text(
                'Diğer Parçalar',
                style: TextStyle(fontWeight: FontWeight.w800),
              ),
              const SizedBox(height: 8),
              _RelatedPartChips(
                parts: other,
                keyPrefix: 'other',
                effective: effective,
                controller: controller,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _RelatedPartChips extends StatelessWidget {
  const _RelatedPartChips({
    required this.parts,
    required this.keyPrefix,
    required this.effective,
    required this.controller,
  });

  final List<MachineRelatedPart> parts;
  final String keyPrefix;
  final EffectivePartSelection? effective;
  final AppController controller;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) => Wrap(
      spacing: 10,
      runSpacing: 10,
      children: [
        for (final part in parts)
          ConstrainedBox(
            constraints: BoxConstraints(maxWidth: constraints.maxWidth),
            child: _RelatedPartChip(
              chipKey: Key('$keyPrefix-part-${part.partId}'),
              part: part,
              selected:
                  effective != null &&
                  !effective!.isMain &&
                  effective!.partId == part.partId,
              controller: controller,
            ),
          ),
      ],
    ),
  );
}

class _RelatedPartChip extends StatelessWidget {
  const _RelatedPartChip({
    required this.chipKey,
    required this.part,
    required this.selected,
    required this.controller,
  });

  final Key chipKey;
  final MachineRelatedPart part;
  final bool selected;
  final AppController controller;

  @override
  Widget build(BuildContext context) => ChoiceChip(
    key: chipKey,
    selected: selected,
    backgroundColor: Colors.white,
    selectedColor: const Color(0xFFFDE68A),
    side: BorderSide(
      color: selected ? const Color(0xFFB45309) : const Color(0xFFCBD5E1),
      width: selected ? 2.5 : 1,
    ),
    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
    label: Text(
      part.partCode,
      overflow: TextOverflow.ellipsis,
      style: TextStyle(
        fontSize: 15,
        fontWeight: selected ? FontWeight.w900 : FontWeight.w700,
      ),
    ),
    onSelected: controller.busy
        ? null
        : (_) => controller.selectEffectivePart(
            EffectivePartSelection(
              partId: part.partId,
              partCode: part.partCode,
              isMain: false,
            ),
          ),
  );
}

class _CompatibleVariantCard extends StatelessWidget {
  const _CompatibleVariantCard({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    if (controller.compatibleVariantsLoading) {
      return const Card(
        key: Key('compatible-variants-loading'),
        child: Padding(
          padding: EdgeInsets.all(20),
          child: Center(child: CircularProgressIndicator()),
        ),
      );
    }
    if (controller.compatibleVariantsError case final error?) {
      return _RetryState(
        key: const Key('compatible-variants-error'),
        retryKey: const Key('compatible-variants-retry'),
        message: error,
        onRetry: controller.retryCompatibleVariants,
      );
    }
    final variants = controller.compatibleVariants;
    if (variants == null) {
      return const SizedBox.shrink();
    }
    if (variants.isEmpty) {
      final batchVariantCode = controller.batchTargetVariantCode;
      return Card(
        key: const Key('zero-compatible-variants'),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Text(
            batchVariantCode == null
                ? 'Uyumlu varyant bulunamadı.'
                : 'Bu parça, grubun $batchVariantCode varyantıyla uyumlu değil.',
            key: const Key('zero-variants-message'),
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
        ),
      );
    }
    // A single compatible variant resolves internally while RELATED/ALT
    // alternatives remain reachable on the same page. It is informational,
    // not a one-item selectable list.
    if (variants.length == 1) {
      final variant = variants.single;
      return Card(
        key: const Key('single-compatible-variant'),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            children: [
              const Icon(Icons.check_circle, color: Color(0xFF15803D)),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  '${variant.variantCode} · ${variant.variantName} (otomatik seçildi)',
                  key: const Key('single-compatible-variant-text'),
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
              ),
            ],
          ),
        ),
      );
    }
    return Card(
      key: const Key('multiple-compatible-variants'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Padding(
            padding: EdgeInsets.fromLTRB(16, 16, 16, 8),
            child: Text(
              'Uyumlu Varyantlar',
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w800),
            ),
          ),
          for (final variant in variants)
            ListTile(
              key: Key('compatible-variant-${variant.variantId}'),
              title: Text('${variant.variantCode} · ${variant.variantName}'),
              trailing:
                  controller.resolvedCompatibleVariant?.variantId ==
                      variant.variantId
                  ? const Icon(Icons.check_circle, color: Color(0xFF15803D))
                  : null,
              onTap: controller.busy
                  ? null
                  : () => controller.selectCompatibleVariant(variant),
            ),
          const SizedBox(height: 8),
        ],
      ),
    );
  }
}

class _RetryState extends StatelessWidget {
  const _RetryState({
    required this.message,
    required this.onRetry,
    required this.retryKey,
    super.key,
  });

  final String message;
  final Future<void> Function() onRetry;
  final Key retryKey;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(message),
          const SizedBox(height: 10),
          OutlinedButton.icon(
            key: retryKey,
            onPressed: () => onRetry(),
            icon: const Icon(Icons.refresh),
            label: const Text('Yeniden Dene'),
          ),
        ],
      ),
    ),
  );
}
