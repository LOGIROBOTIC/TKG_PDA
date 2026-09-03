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
            Expanded(child: _QueueBody(controller: controller)),
          ],
        ),
      ),
    ),
  );
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
              itemBuilder: (context, index) => _QueueCard(item: items[index]),
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
  const _QueueCard({required this.item});

  final OperatorQueueItem item;

  @override
  Widget build(BuildContext context) => Card(
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
          Text(_etaText(item)),
        ],
      ),
      trailing: Chip(label: Text(_stateText(item.state))),
    ),
  );
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
  'REQUESTED' => 'İstendi',
  'QUEUED' => 'Sırada',
  'ROBOT_ASSIGNED' => 'Robot atandı',
  'IN_PROGRESS' => 'Devam ediyor',
  'COMPLETED' => 'Tamamlandı',
  'ERROR' => 'Hata',
  'CANCELLED' => 'İptal',
  _ => 'Güncelleniyor',
};
