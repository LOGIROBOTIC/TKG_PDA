import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../data/models/pda_models.dart';

/// WP-33 business-action surface. It consumes only the backend's logical
/// position projection and never receives a physical slot identifier.
class ContainerPositionPanel extends StatelessWidget {
  const ContainerPositionPanel({required this.controller, super.key});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    if (controller.containerPositionsLoading &&
        controller.containerPositions.isEmpty) {
      return const SizedBox(
        height: 104,
        child: Center(child: CircularProgressIndicator()),
      );
    }
    if (controller.containerPositionsError case final error?) {
      return Padding(
        padding: const EdgeInsets.all(12),
        child: Row(
          children: [
            Expanded(child: Text(error)),
            TextButton(
              key: const Key('retry-container-positions'),
              onPressed: controller.retryContainerPositions,
              child: const Text('Yenile'),
            ),
          ],
        ),
      );
    }
    final positions = controller.containerPositions;
    if (positions.isEmpty) {
      return const Padding(
        padding: EdgeInsets.all(14),
        child: Text(
          'Bu parça için doğrulanmış kasa konumu bulunmuyor.',
          key: Key('no-container-positions'),
          textAlign: TextAlign.center,
        ),
      );
    }
    return Material(
      key: const Key('container-position-panel'),
      elevation: 10,
      color: Theme.of(context).colorScheme.surface,
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(8, 4, 8, 4),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SizedBox(
                height: 124,
                child: ListView.separated(
                  key: const Key('container-position-list'),
                  scrollDirection: Axis.horizontal,
                  itemCount: positions.length,
                  separatorBuilder: (_, _) => const SizedBox(width: 8),
                  itemBuilder: (context, index) => SizedBox(
                    width: 238,
                    child: _PositionCard(
                      controller: controller,
                      position: positions[index],
                    ),
                  ),
                ),
              ),
              if (!controller.isConfiguringBatchItem) ...[
                const SizedBox(height: 4),
                Card(
                  elevation: 0,
                  color: Theme.of(context)
                      .colorScheme
                      .surfaceContainerHighest
                      .withValues(alpha: 0.45),
                  shape: RoundedRectangleBorder(
                    borderRadius: BorderRadius.circular(8),
                    side: BorderSide(
                      color: Theme.of(context).colorScheme.outlineVariant,
                    ),
                  ),
                  child: Padding(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 4,
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Row(
                          children: [
                            const Icon(
                              Icons.warning_amber_rounded,
                              size: 14,
                              color: Colors.orange,
                            ),
                            const SizedBox(width: 4),
                            Expanded(
                              child: Text(
                                'Varyant Kasa Yönetimi (${controller.context?.activeVariantCode ?? 'Aktif Varyant'})'
                                '${positions.isNotEmpty ? ' · ${positions.length} Kasa' : ''}',
                                style: const TextStyle(
                                  fontWeight: FontWeight.bold,
                                  fontSize: 10,
                                ),
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                              ),
                            ),
                            if (!controller.mutationGate.canMutate)
                              Text(
                                controller.mutationGate.message ??
                                    'İşlem kilitli',
                                style: TextStyle(
                                  fontSize: 9,
                                  color: Theme.of(context).colorScheme.error,
                                  fontWeight: FontWeight.w500,
                                ),
                              ),
                          ],
                        ),
                        const SizedBox(height: 4),
                        OutlinedButton.icon(
                          key: const Key('remove-all-containers'),
                          style: OutlinedButton.styleFrom(
                            visualDensity: VisualDensity.compact,
                            padding: const EdgeInsets.symmetric(vertical: 4),
                          ),
                          onPressed:
                              controller.mutationGate.canMutate &&
                                  !controller.removeAllSubmitting
                              ? controller.removeAllContainers
                              : null,
                          icon: controller.removeAllSubmitting
                              ? const SizedBox.square(
                                  dimension: 14,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                )
                              : const Icon(Icons.clear_all, size: 15),
                          label: const Text(
                            'Tüm Kasaları Kaldır',
                            style: TextStyle(fontSize: 12),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _PositionCard extends StatelessWidget {
  const _PositionCard({required this.controller, required this.position});

  final AppController controller;
  final ContainerPosition position;

  @override
  Widget build(BuildContext context) {
    final totalPositions = controller.containerPositions.length;
    final positionLabel = totalPositions > 1
        ? '${position.label} (${position.positionNumber} / $totalPositions)'
        : position.label;

    return Card(
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.all(6),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    positionLabel,
                    style: const TextStyle(fontWeight: FontWeight.w900),
                  ),
                ),
                _StateBadge(position: position),
              ],
            ),
            const SizedBox(height: 6),
            Expanded(
              child: Row(
                children: [
                  Expanded(
                    child: _BusinessActionButton(
                      controller: controller,
                      position: position,
                      action: 'FULL',
                      label: '${position.positionNumber}. Kasa Doldu',
                      availability: position.fullAction,
                      icon: Icons.inventory,
                    ),
                  ),
                  const SizedBox(width: 6),
                  Expanded(
                    child: _BusinessActionButton(
                      controller: controller,
                      position: position,
                      action: 'EMPTY',
                      label: '${position.positionNumber}. Kasa Boş',
                      availability: position.emptyAction,
                      icon: Icons.move_to_inbox_outlined,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _BusinessActionButton extends StatelessWidget {
  const _BusinessActionButton({
    required this.controller,
    required this.position,
    required this.action,
    required this.label,
    required this.availability,
    required this.icon,
  });

  final AppController controller;
  final ContainerPosition position;
  final String action;
  final String label;
  final ContainerActionAvailability availability;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final submitting = controller.isSubmittingContainerAction(
      position.positionNumber,
      action,
    );
    final enabled =
        availability.enabled &&
        controller.mutationGate.canMutate &&
        !submitting;

    final disabledReason = !controller.mutationGate.canMutate
        ? (controller.mutationGate.message ?? 'İşlem kilitli')
        : (!availability.enabled
            ? (availability.disabledMessage ?? 'Bu kasa için işlem uygun değil')
            : null);

    return Tooltip(
      key: Key('container-${position.positionNumber}-$action'),
      message: enabled ? label : (disabledReason ?? label),
      child: ElevatedButton(
        // The key names the LOGICAL business action, which under WP-33 is
        // (machine, variant/layout, part, logical container number, action) — so the
        // container number has to be part of it.
        key: Key(
          action == 'FULL'
              ? 'direct-action-REMOVE_FULL-${position.positionNumber}'
              : 'direct-action-BRING_EMPTY-${position.positionNumber}',
        ),
        onPressed: enabled
            ? () => controller.submitContainerAction(position, action)
            : null,
        style: ElevatedButton.styleFrom(
          padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
        ),
        child: submitting
            ? const Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  SizedBox.square(
                    dimension: 19,
                    child: CircularProgressIndicator(
                      strokeWidth: 2.5,
                      color: Colors.white,
                    ),
                  ),
                  SizedBox(height: 4),
                  Text('Gönderiliyor', textAlign: TextAlign.center),
                ],
              )
            : Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(icon, size: enabled ? 22 : 18),
                  const SizedBox(height: 2),
                  Text(
                    label,
                    maxLines: 1,
                    textAlign: TextAlign.center,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 12,
                      fontWeight: enabled ? FontWeight.w600 : FontWeight.normal,
                    ),
                  ),
                  if (!enabled && disabledReason != null) ...[
                    const SizedBox(height: 2),
                    Text(
                      disabledReason,
                      maxLines: 1,
                      textAlign: TextAlign.center,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 9,
                        color: Theme.of(context).colorScheme.error,
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                  ],
                ],
              ),
      ),
    );
  }
}

class _StateBadge extends StatefulWidget {
  const _StateBadge({required this.position});

  final ContainerPosition position;

  @override
  State<_StateBadge> createState() => _StateBadgeState();
}

class _StateBadgeState extends State<_StateBadge>
    with SingleTickerProviderStateMixin {
  AnimationController? _controller;

  @override
  void initState() {
    super.initState();
    _syncAnimation();
  }

  @override
  void didUpdateWidget(_StateBadge old) {
    super.didUpdateWidget(old);
    if (old.position.transportState != widget.position.transportState) {
      _syncAnimation();
    }
  }

  void _syncAnimation() {
    final state = widget.position.transportState;
    final shouldAnimate =
        state == 'REQUESTED' || state == 'IN_PROGRESS' || state == 'QUEUED';
    if (!shouldAnimate) {
      _controller?.dispose();
      _controller = null;
      return;
    }
    _controller ??= AnimationController(vsync: this);
    _controller!
      ..duration = state == 'REQUESTED'
          ? const Duration(milliseconds: 1200)
          : const Duration(milliseconds: 2400)
      ..repeat(reverse: true);
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final suffix = widget.position.queuePosition == null
        ? ''
        : ' · #${widget.position.queuePosition}';
    final disableAnimations = MediaQuery.of(context).disableAnimations;
    final chip = Chip(
      visualDensity: VisualDensity.compact,
      label: Text(
        '${_stateLabel(widget.position.transportState)}$suffix',
        style: const TextStyle(fontSize: 10, fontWeight: FontWeight.w800),
      ),
    );
    if (_controller == null || disableAnimations) {
      return Semantics(
        label: 'Taşıma durumu ${widget.position.transportState}$suffix',
        child: chip,
      );
    }
    return Semantics(
      label: 'Taşıma durumu ${widget.position.transportState}$suffix',
      child: FadeTransition(
        opacity: Tween<double>(begin: 0.45, end: 1.0).animate(
          CurvedAnimation(parent: _controller!, curve: Curves.easeInOut),
        ),
        child: chip,
      ),
    );
  }
}

String _stateLabel(String state) => switch (state) {
  'READY' => 'Hazır',
  'WAITING_PREPARATION' => 'Hazırlık Bekleniyor',
  'REQUESTED' => 'İstendi',
  'QUEUED' => 'Sırada',
  'ROBOT_ASSIGNED' => 'Robot atandı',
  'IN_PROGRESS' => 'Devam ediyor',
  'COMPLETED' => 'Tamamlandı',
  'ERROR' => 'Hata',
  _ => 'Güncelleniyor',
};
