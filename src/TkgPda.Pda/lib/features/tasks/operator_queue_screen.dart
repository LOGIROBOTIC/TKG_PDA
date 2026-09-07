import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../data/models/pda_models.dart';
import '../../shared/widgets/feedback_banners.dart';

class OperatorQueueScreen extends StatelessWidget {
  const OperatorQueueScreen({required this.controller, super.key});

  final AppController controller;

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: controller,
    builder: (context, _) => Scaffold(
      appBar: AppBar(
        leading: IconButton(
          key: const Key('leave-operator-queue'),
          onPressed: controller.leaveTasks,
          icon: const Icon(Icons.arrow_back),
        ),
        title: const Text('Görevler'),
        actions: [
          IconButton(
            key: const Key('refresh-operator-queue'),
            onPressed: controller.operatorQueueLoading
                ? null
                : controller.loadOperatorQueue,
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          children: [
            if (controller.offline) const OfflineBanner(),
            Padding(
              padding: const EdgeInsets.all(12),
              child: SegmentedButton<bool>(
                segments: const [
                  ButtonSegment(value: false, label: Text('Aktif')),
                  ButtonSegment(value: true, label: Text('Tamamlananlar')),
                ],
                selected: {controller.operatorQueueTerminal},
                onSelectionChanged: controller.operatorQueueLoading
                    ? null
                    : (values) =>
                          controller.setOperatorQueueTerminal(values.single),
              ),
            ),
            _RobotStatusBar(controller: controller),
            Expanded(child: _QueueBody(controller: controller)),
          ],
        ),
      ),
    ),
  );
}

class _RobotStatusBar extends StatelessWidget {
  const _RobotStatusBar({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final robots = controller.robots;
    if (robots.isEmpty) return const SizedBox.shrink();

    return Column(
      key: const Key('robot-status-bar'),
      mainAxisSize: MainAxisSize.min,
      children: [
        for (final robot in robots)
          _RobotCard(robot: robot),
      ],
    );
  }
}

class _RobotCard extends StatelessWidget {
  const _RobotCard({required this.robot});

  final RobotTelemetry robot;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final battery = robot.batteryLevel;
    // Unavailable battery telemetry shows as unknown, never as a 0% reading.
    final batteryLabel = battery == null ? '—' : '${(battery * 100).toInt()}%';

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
      elevation: 0,
      color: theme.colorScheme.surfaceContainerHighest.withValues(alpha: 0.5),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: BorderSide(color: theme.colorScheme.outlineVariant),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        child: Row(
          children: [
            Icon(
              Icons.precision_manufacturing,
              color: robot.isConnected ? Colors.green.shade700 : Colors.grey,
              size: 20,
            ),
            const SizedBox(width: 8),
            Text(
              robot.displayName ?? 'Robot',
              style: theme.textTheme.labelLarge?.copyWith(fontWeight: FontWeight.bold),
            ),
            const SizedBox(width: 8),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
              decoration: BoxDecoration(
                color: robot.isConnected
                    ? theme.colorScheme.primaryContainer
                    : theme.colorScheme.errorContainer,
                borderRadius: BorderRadius.circular(6),
              ),
              child: Text(
                robot.isConnected ? 'Çevrimiçi' : 'Çevrimdışı',
                style: theme.textTheme.labelSmall,
              ),
            ),
            const SizedBox(width: 6),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
              decoration: BoxDecoration(
                color: theme.colorScheme.primaryContainer,
                borderRadius: BorderRadius.circular(6),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.battery_std, size: 14),
                  const SizedBox(width: 2),
                  Text(batteryLabel, style: theme.textTheme.labelSmall),
                ],
              ),
            ),
            const Spacer(),
            if (robot.hasActiveAlarm)
              Tooltip(
                message: 'Robot arızası mevcut',
                child: Icon(
                  Icons.warning_amber_rounded,
                  size: 20,
                  color: theme.colorScheme.error,
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _QueueBody extends StatelessWidget {
  const _QueueBody({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    if (controller.operatorQueueLoading && controller.operatorQueue == null) {
      return const Center(child: CircularProgressIndicator());
    }
    if (controller.operatorQueueError case final error?) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Text(error, textAlign: TextAlign.center),
        ),
      );
    }
    final page = controller.operatorQueue;
    final items = page?.items ?? const <OperatorQueueItem>[];
    if (items.isEmpty) {
      return Center(
        child: Text(
          controller.operatorQueueTerminal
              ? 'Yakın zamanda tamamlanan görev yok.'
              : 'Aktif görev yok.',
          key: const Key('operator-queue-empty'),
        ),
      );
    }
    return Column(
      children: [
        Expanded(
          child: RefreshIndicator(
            onRefresh: controller.loadOperatorQueue,
            child: ListView.separated(
              key: const Key('operator-queue-list'),
              padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
              itemCount: items.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) => _QueueCard(
                controller: controller,
                item: items[index],
              ),
            ),
          ),
        ),
        if (page != null && (page.offset > 0 || page.hasMore))
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 4, 12, 12),
            child: Row(
              children: [
                Expanded(
                  child: OutlinedButton(
                    key: const Key('operator-queue-previous'),
                    onPressed: page.offset > 0
                        ? () => controller.loadOperatorQueue(
                            offset: (page.offset - page.limit).clamp(
                              0,
                              page.offset,
                            ),
                          )
                        : null,
                    child: const Text('Önceki'),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: OutlinedButton(
                    key: const Key('operator-queue-next'),
                    onPressed: page.hasMore
                        ? () => controller.loadOperatorQueue(
                            offset: page.offset + page.limit,
                          )
                        : null,
                    child: const Text('Sonraki'),
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }
}

class _QueueCard extends StatelessWidget {
  const _QueueCard({
    required this.controller,
    required this.item,
  });

  final AppController controller;
  final OperatorQueueItem item;

  @override
  Widget build(BuildContext context) {
    final canCancel = !controller.operatorQueueTerminal &&
        item.requestId != null &&
        _isCancelable(item.state);
    return Card(
      child: ListTile(
        leading: CircleAvatar(child: Text('${item.queuePosition}')),
        title: Text(
          item.machine,
          style: const TextStyle(fontWeight: FontWeight.w900),
        ),
        subtitle: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(item.part),
            Text(
              '${item.logicalContainerNumber == null ? 'Kasa' : '${item.logicalContainerNumber}. Kasa'} · ${item.businessAction}',
            ),
            Text(_requestTime(item.requestedAt)),
            Text(
              _etaText(item),
              style: TextStyle(
                color: item.etaState == 'AVAILABLE'
                    ? Theme.of(context).colorScheme.primary
                    : null,
                fontWeight: item.etaState == 'AVAILABLE'
                    ? FontWeight.w600
                    : null,
              ),
            ),
          ],
        ),
        trailing: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Chip(label: Text(_stateText(item.state))),
            if (canCancel) ...[
              const SizedBox(width: 4),
              IconButton(
                key: Key('cancel-queue-${item.requestId}'),
                icon: const Icon(Icons.cancel_outlined, color: Colors.redAccent),
                tooltip: 'Görevi İptal Et',
                onPressed: controller.mutationEnabled && !controller.busy
                    ? () => _confirmCancel(context)
                    : null,
              ),
            ],
          ],
        ),
      ),
    );
  }

  bool _isCancelable(String state) => switch (state) {
    'WAITING_PREPARATION' || 'REQUESTED' || 'QUEUED' || 'ROBOT_ASSIGNED' || 'IN_PROGRESS' => true,
    _ => false,
  };

  Future<void> _confirmCancel(BuildContext context) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Taşıma İptali'),
        content: Text(
          '${item.machine} hücresine ait ${item.part} taşıma talebini iptal etmek istediğinize emin misiniz?',
        ),
        actions: [
          TextButton(
            key: const Key('dismiss-cancel-queue'),
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: const Text('Vazgeç'),
          ),
          ElevatedButton(
            key: const Key('confirm-cancel-queue'),
            style: ElevatedButton.styleFrom(
              backgroundColor: Colors.red,
              foregroundColor: Colors.white,
            ),
            onPressed: () => Navigator.of(dialogContext).pop(true),
            child: const Text('İptal Et'),
          ),
        ],
      ),
    );

    if (confirmed == true && item.requestId != null) {
      await controller.cancelQueueItem(item.requestId!);
    }
  }
}

String _requestTime(DateTime value) {
  final local = value.toLocal();
  String two(int part) => part.toString().padLeft(2, '0');
  return '${two(local.day)}.${two(local.month)}.${local.year} ${two(local.hour)}:${two(local.minute)}';
}

String _etaText(OperatorQueueItem item) {
  if (item.etaState != 'AVAILABLE' || item.etaSeconds == null) {
    return 'Süre bilgisi yok';
  }
  final minutes = (item.etaSeconds! / 60).ceil();
  return minutes < 1 ? '1 dakikadan az' : 'Yaklaşık $minutes dakika';
}

String _stateText(String state) => switch (state) {
  'WAITING_PREPARATION' => 'Hazırlık Bekleniyor',
  'REQUESTED' => 'İstendi',
  'QUEUED' => 'Sırada',
  'ROBOT_ASSIGNED' => 'Robot atandı',
  'IN_PROGRESS' => 'Devam ediyor',
  'COMPLETED' => 'Tamamlandı',
  'ERROR' => 'Hata',
  'CANCELLED' => 'İptal',
  _ => 'Güncelleniyor',
};

