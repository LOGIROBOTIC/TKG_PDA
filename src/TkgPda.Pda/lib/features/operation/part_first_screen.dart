import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../data/models/pda_models.dart';
import '../../shared/presentation.dart';

/// WP-03B: the part-first gate shown after a machine is selected and before
/// the existing variant/layout/slot/operation flow. Reuses the existing
/// backend endpoints (part-options, compatible-variants) and the existing
/// variant-selection mechanism; never duplicates compatibility logic.
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
        padding: const EdgeInsets.all(14),
        children: [
          const Text(
            'Parça Seçimi',
            style: TextStyle(fontSize: 20, fontWeight: FontWeight.w900),
          ),
          const SizedBox(height: 6),
          const Text('İşlem yapılacak ana parçayı seçin.'),
          const SizedBox(height: 14),
          if (controller.partOptions.isEmpty)
            const Text('Bu makine için tanımlı parça bulunamadı.')
          else
            _MainPartChips(controller: controller),
          if (controller.selectedMainPart case final main?) ...[
            const SizedBox(height: 20),
            _EffectivePartCard(main: main, controller: controller),
          ],
          if (controller.selectedEffectivePart != null) ...[
            const SizedBox(height: 20),
            _CompatibleVariantCard(controller: controller),
          ],
          const SizedBox(height: 40),
        ],
      ),
    );
  }
}

class _MainPartChips extends StatelessWidget {
  const _MainPartChips({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) => Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [
        for (final option in controller.partOptions)
          ConstrainedBox(
            constraints: BoxConstraints(maxWidth: constraints.maxWidth),
            child: ChoiceChip(
              key: Key('main-part-${option.partId}'),
              selected: controller.selectedMainPart?.partId == option.partId,
              label: Text(
                '${option.partCode} · ${knownOr(option.description)}',
                overflow: TextOverflow.ellipsis,
                maxLines: 2,
              ),
              onSelected: (_) => controller.selectMainPartOption(option),
            ),
          ),
      ],
    ),
  );
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
          (part) => part.relationType != 'RELATED' && part.relationType != 'ALT_PART',
        )
        .toList(growable: false);

    return Card(
      key: Key('main-part-detail-${main.partId}'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Parça · ${main.partCode}',
              style: const TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.w800,
              ),
            ),
            const SizedBox(height: 10),
            ChoiceChip(
              key: const Key('effective-part-main'),
              selected: effective?.isMain ?? false,
              label: Text('Ana Parçayı Kullan · ${main.partCode}'),
              onSelected: (_) => controller.selectEffectivePart(
                EffectivePartSelection(
                  partId: main.partId,
                  partCode: main.partCode,
                  isMain: true,
                ),
              ),
            ),
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
      spacing: 8,
      runSpacing: 8,
      children: [
        for (final part in parts)
          ConstrainedBox(
            constraints: BoxConstraints(maxWidth: constraints.maxWidth),
            child: ChoiceChip(
              key: Key('$keyPrefix-part-${part.partId}'),
              selected:
                  effective != null &&
                  !effective!.isMain &&
                  effective!.partId == part.partId,
              label: Text(part.partCode, overflow: TextOverflow.ellipsis),
              onSelected: (_) => controller.selectEffectivePart(
                EffectivePartSelection(
                  partId: part.partId,
                  partCode: part.partCode,
                  isMain: false,
                ),
              ),
            ),
          ),
      ],
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
      return const Card(
        key: Key('zero-compatible-variants'),
        child: Padding(
          padding: EdgeInsets.all(16),
          child: Text(
            'Uyumlu varyant bulunamadı.',
            key: Key('zero-variants-message'),
            style: TextStyle(fontWeight: FontWeight.w700),
          ),
        ),
      );
    }
    // Exactly one compatible variant auto-resolves inside the controller;
    // compatibleVariantsLoading stays true for that whole request (handled
    // above), so reaching this branch only ever happens with 2+ variants.
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
