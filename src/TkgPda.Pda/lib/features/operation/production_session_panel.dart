import 'package:flutter/material.dart';

import '../../app/app_controller.dart';

/// Production lifecycle is independent of transport execution. These controls
/// never cancel a request or instruct a robot.
class ProductionSessionPanel extends StatelessWidget {
  const ProductionSessionPanel({required this.controller, super.key});
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final state = controller.production;
    final active = state.active;
    final openCount = controller.context?.activeTransportRequests.length ?? 0;
    final error = controller.productionContextError ?? state.error;
    return Card(
      key: const Key('production-panel'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              active == null ? 'Üretim' : 'Üretim Devam Ediyor',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 8),
            if (state.loading || state.inFlight) ...[
              const LinearProgressIndicator(),
              Text(
                state.inFlight
                    ? 'Üretim işlemi doğrulanıyor…'
                    : 'Üretim durumu kontrol ediliyor…',
              ),
            ],
            if (active != null) ...[
              Text(
                'Ana ürün: ${controller.selectedMainPart?.partCode ?? 'Doğrulanıyor…'}',
              ),
              Text(
                'Varyant: ${controller.context?.activeVariantCode ?? 'Doğrulanıyor…'}',
              ),
              Text('Başlangıç: ${_time(active.startedAt.toLocal())}'),
              const Text('Ana ürün ve varyant üretim bitene kadar sabittir.'),
            ] else
              const Text(
                'Ana ürünü ve uyumlu varyantı seçip üretimi başlatın. Taşıma taleplerini ardından oluşturabilirsiniz.',
              ),
            if (error != null) ...[
              const SizedBox(height: 8),
              Text(error, key: const Key('production-error')),
            ],
            if (state.hasPending) ...[
              const SizedBox(height: 8),
              Text(
                '${state.pendingLabel} sonucu kesinleşmedi. Yeniden deneme aynı işlemi doğrular; yeni üretim oluşturmaz.',
              ),
              OutlinedButton.icon(
                key: const Key('production-retry'),
                onPressed: controller.isMutationInFlight
                    ? null
                    : controller.retryProduction,
                icon: const Icon(Icons.refresh),
                label: const Text('Aynı İşlemi Yeniden Dene'),
              ),
            ] else if (active == null) ...[
              const SizedBox(height: 12),
              FilledButton.icon(
                key: const Key('production-start'),
                style: FilledButton.styleFrom(minimumSize: const Size(0, 52)),
                onPressed: controller.canStartProduction
                    ? controller.startProduction
                    : null,
                icon: const Icon(Icons.play_arrow),
                label: const Text('Üretime Başla'),
              ),
              if (openCount > 0)
                const Text(
                  'Yeni üretim için açık taşıma taleplerinin tamamlanmasını bekleyin veya talepleri ayrıca iptal edin.',
                ),
            ] else ...[
              const SizedBox(height: 12),
              OutlinedButton.icon(
                key: const Key('production-end'),
                style: OutlinedButton.styleFrom(minimumSize: const Size(0, 52)),
                onPressed: controller.canEndProduction
                    ? () => _confirmEnd(context)
                    : null,
                icon: const Icon(Icons.stop_circle_outlined),
                label: const Text('Üretimi Bitir'),
              ),
              if (openCount > 0)
                Text(
                  '$openCount açık taşıma var. Üretimi bitirmek için tamamlanmalarını bekleyin veya talepleri ayrıca iptal edin.',
                  key: const Key('production-open-transports'),
                ),
              if (controller.batchCart.isNotEmpty)
                const Text(
                  'Önce sepetteki taşıma seçimlerini gönderin veya sepeti temizleyin.',
                ),
              if (controller.hasAmbiguousSubmission ||
                  controller.hasAmbiguousBatchSubmission)
                const Text('Önce sonucu belirsiz taşıma talebini doğrulayın.'),
            ],
            if (!state.verified &&
                !state.loading &&
                !state.inFlight &&
                !state.hasPending)
              TextButton.icon(
                key: const Key('production-refresh'),
                onPressed: () => controller.refreshContext(),
                icon: const Icon(Icons.sync),
                label: const Text('Üretim Durumunu Yenile'),
              ),
          ],
        ),
      ),
    );
  }

  Future<void> _confirmEnd(BuildContext context) async {
    final sessionId = controller.production.active?.id;
    final accepted = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Üretimi bitir?'),
        content: const Text(
          'Bu makinenin mevcut üretimi kapatılacak. Bu işlem taşıma görevlerini tamamlamaz veya iptal etmez.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Vazgeç'),
          ),
          FilledButton(
            key: const Key('confirm-production-end'),
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Üretimi Bitir'),
          ),
        ],
      ),
    );
    // Another tablet may have changed production while this dialog was open.
    if (accepted == true &&
        context.mounted &&
        sessionId == controller.production.active?.id) {
      await controller.endProduction();
    }
  }

  static String _time(DateTime value) {
    String two(int number) => number.toString().padLeft(2, '0');
    return '${two(value.day)}.${two(value.month)}.${value.year} ${two(value.hour)}:${two(value.minute)}';
  }
}
