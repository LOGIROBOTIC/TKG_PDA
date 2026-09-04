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
          padding: const EdgeInsets.fromLTRB(10, 8, 10, 8),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SizedBox(
                height: 154,
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
                const SizedBox(height: 8),
                OutlinedButton.icon(
                  key: const Key('remove-all-containers'),
                  onPressed:
                      controller.mutationEnabled &&
                          !controller.removeAllSubmitting
                      ? controller.removeAllContainers
                      : null,
                  icon: controller.removeAllSubmitting
                      ? const SizedBox.square(
                          dimension: 18,
                          child: CircularProgressIndicator(strokeWidth: 2.5),
                        )
                      : const Icon(Icons.clear_all),
                  label: const Text('Tüm Kasaları Kaldır'),
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
  Widget build(BuildContext context) => Card(
    margin: EdgeInsets.zero,
    child: Padding(
      padding: const EdgeInsets.all(9),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  position.label,
                  style: const TextStyle(fontWeight: FontWeight.w900),
                ),
              ),
              _StateBadge(position: position),
            ],
          ),
          const SizedBox(height: 8),
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
        availability.enabled && controller.mutationEnabled && !submitting;
    return Tooltip(
      key: Key('container-${position.positionNumber}-$action'),
      message: enabled ? label : availability.disabledMessage ?? label,
      child: ElevatedButton(
        key: Key(
          action == 'FULL'
              ? 'direct-action-REMOVE_FULL'
              : 'direct-action-BRING_EMPTY',
        ),
        onPressed: enabled
            ? () => controller.submitContainerAction(position, action)
            : null,
        style: ElevatedButton.styleFrom(
          padding: const EdgeInsets.symmetric(horizontal: 5, vertical: 6),
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
                  Icon(icon, size: 22),
                  const SizedBox(height: 3),
                  Text(
                    label,
                    maxLines: 2,
                    textAlign: TextAlign.center,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontSize: 12),
                  ),
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
