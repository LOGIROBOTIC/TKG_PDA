import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../shared/widgets/feedback_banners.dart';
import 'container_position_panel.dart';
import 'part_first_screen.dart';

/// Machine-scoped Operator workspace. Part/variant resolution and transport
/// actions share one page; there is no second physical-layout screen.
class OperationScreen extends StatelessWidget {
  const OperationScreen({required this.controller, super.key});

  final AppController controller;

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: controller,
    builder: (context, _) => Scaffold(
      appBar: AppBar(
        leading: IconButton(
          key: const Key('leave-machine'),
          tooltip: 'Makine listesi',
          onPressed: controller.busy ? null : controller.leaveMachine,
          icon: const Icon(Icons.arrow_back),
        ),
        title: Text(
          controller.selectedMachine == null
              ? 'Parça ve İşlem'
              : controller.batchMode
              ? '${controller.selectedMachine!.code} · '
                    '${controller.batchCart.length} Parça Seçili'
              : '${controller.selectedMachine!.code} · Parça ve İşlem',
        ),
        actions: [
          IconButton(
            key: const Key('enter-batch-selection'),
            tooltip: controller.batchMode
                ? 'Toplu Seçim Modunu Kapat'
                : 'Birden Fazla Parça / Toplu Mod',
            onPressed: controller.busy ? null : controller.toggleBatchMode,
            icon: Badge(
              isLabelVisible: controller.batchCart.isNotEmpty,
              label: Text('${controller.batchCart.length}'),
              child: Icon(
                controller.batchMode
                    ? Icons.playlist_add_check
                    : Icons.playlist_add,
                color: controller.batchMode ? Colors.amberAccent : null,
              ),
            ),
          ),
          IconButton(
            key: const Key('open-operator-queue-from-machine'),
            tooltip: 'Görevler',
            onPressed: controller.busy ? null : controller.enterTasks,
            icon: const Icon(Icons.format_list_numbered),
          ),
          IconButton(
            tooltip: 'Güncelle',
            onPressed: controller.busy
                ? null
                : () => controller.refreshContext(showBusy: true),
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: SafeArea(
        child: Column(
          children: [
            if (controller.offline) const OfflineBanner(),
            if (controller.problem case final problem?)
              ProblemBanner(problem: problem),
            if (controller.notice case final notice?)
              NoticeBanner(message: notice),
            RobotAlarmBanner(
              summary: controller.robotAlarmSummary,
              error: controller.robotAlarmError,
              onRetry: controller.retryRobotAlarmSummary,
            ),
            if ((controller.context?.activeTransportRequests.length ?? 0) > 0)
              _ActiveTransportIndicator(
                count: controller.context!.activeTransportRequests.length,
              ),
            if (controller.hasAmbiguousSubmission)
              _AmbiguousSubmissionBanner(controller: controller),
            if (controller.hasAmbiguousBatchSubmission)
              _AmbiguousBatchBanner(controller: controller),
            if (controller.isConfiguringBatchItem)
              _BatchConfiguringBanner(controller: controller),
            Expanded(
              child: _PartFirstWithBatchSkip(controller: controller),
            ),
            if (controller.batchMode || controller.batchCart.isNotEmpty)
              BatchCartTray(controller: controller),
            if (controller.busy) const LinearProgressIndicator(minHeight: 5),
          ],
        ),
      ),
    ),
  );
}

class _ActiveTransportIndicator extends StatelessWidget {
  const _ActiveTransportIndicator({required this.count});

  final int count;

  @override
  Widget build(BuildContext context) => Container(
    key: const Key('active-transport-indicator'),
    width: double.infinity,
    color: const Color(0xFFEFF6FF),
    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 7),
    child: Row(
      children: [
        const Icon(Icons.local_shipping_outlined, size: 20),
        const SizedBox(width: 8),
        Text(
          'Aktif taşıma: $count',
          style: const TextStyle(fontWeight: FontWeight.w800),
        ),
      ],
    ),
  );
}

class _PartFirstWithBatchSkip extends StatelessWidget {
  const _PartFirstWithBatchSkip({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) => Column(
    children: [
      Expanded(child: PartFirstSelector(controller: controller)),
      if (controller.transportActionsReady)
        ContainerPositionPanel(controller: controller),
      if (controller.isConfiguringBatchItem &&
          (controller.compatibleVariants?.isEmpty ?? false))
        Padding(
          padding: const EdgeInsets.all(14),
          child: OutlinedButton.icon(
            key: const Key('skip-invalid-batch-item'),
            onPressed: controller.busy ? null : controller.skipInvalidBatchItem,
            icon: const Icon(Icons.remove_circle_outline),
            label: const Text('Bu Parçayı Kaldır ve Devam Et'),
          ),
        ),
    ],
  );
}

class _BatchConfiguringBanner extends StatelessWidget {
  const _BatchConfiguringBanner({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) => Container(
    key: const Key('batch-configuring-banner'),
    width: double.infinity,
    color: const Color(0xFFE0F2FE),
    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          'Grup: Parça ${controller.currentBatchItemPosition}/${controller.batchCart.length} yapılandırılıyor · '
          '${controller.currentBatchItem?.effectivePart.partCode ?? ''}',
          style: const TextStyle(fontWeight: FontWeight.w800),
        ),
        const SizedBox(height: 6),
        _BatchProgressDots(controller: controller),
      ],
    ),
  );
}

class _BatchProgressDots extends StatelessWidget {
  const _BatchProgressDots({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) => Wrap(
    key: const Key('batch-progress-dots'),
    spacing: 6,
    runSpacing: 6,
    children: [
      for (final (index, item) in controller.batchCart.indexed)
        Tooltip(
          message: item.effectivePart.partCode,
          child: Container(
            key: Key('batch-progress-dot-$index'),
            width: 22,
            height: 22,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: item.isConfigured ? const Color(0xFF166534) : Colors.white,
              border: Border.all(
                color: index == controller.currentBatchItemPosition - 1
                    ? const Color(0xFFF59E0B)
                    : const Color(0xFF94A3B8),
                width: index == controller.currentBatchItemPosition - 1 ? 3 : 1,
              ),
            ),
            alignment: Alignment.center,
            child: item.isConfigured
                ? const Icon(Icons.check, size: 13, color: Colors.white)
                : Text(
                    '${index + 1}',
                    style: const TextStyle(
                      fontSize: 11,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
          ),
        ),
    ],
  );
}

class _AmbiguousSubmissionBanner extends StatelessWidget {
  const _AmbiguousSubmissionBanner({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) => Container(
    key: const Key('ambiguous-submission-banner'),
    color: const Color(0xFFFFEDD5),
    padding: const EdgeInsets.all(12),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(
          'Son gönderimin sonucu belirsiz. Yeni anahtar üretilmeyecek.',
          style: TextStyle(fontWeight: FontWeight.w900),
        ),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          children: [
            OutlinedButton(
              key: const Key('retry-ambiguous'),
              onPressed: controller.busy
                  ? null
                  : controller.retryAmbiguousTransport,
              child: const Text('Aynı İsteği Yeniden Dene'),
            ),
            OutlinedButton(
              key: const Key('refresh-after-ambiguous'),
              onPressed: controller.busy
                  ? null
                  : () => controller.refreshContext(showBusy: true),
              child: const Text('Sunucu Durumunu Yenile'),
            ),
          ],
        ),
      ],
    ),
  );
}

class _AmbiguousBatchBanner extends StatelessWidget {
  const _AmbiguousBatchBanner({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) => Container(
    key: const Key('ambiguous-batch-banner'),
    color: const Color(0xFFFFEDD5),
    padding: const EdgeInsets.all(12),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(
          'Son grup gönderiminin sonucu belirsiz. Yeni anahtar üretilmeyecek.',
          style: TextStyle(fontWeight: FontWeight.w900),
        ),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          children: [
            OutlinedButton(
              key: const Key('retry-ambiguous-batch'),
              onPressed: controller.busy
                  ? null
                  : controller.retryAmbiguousBatch,
              child: const Text('Aynı Grubu Yeniden Dene'),
            ),
            OutlinedButton(
              key: const Key('refresh-after-ambiguous-batch'),
              onPressed: controller.busy
                  ? null
                  : () => controller.refreshContext(showBusy: true),
              child: const Text('Sunucu Durumunu Yenile'),
            ),
          ],
        ),
      ],
    ),
  );
}

class BatchCartTray extends StatefulWidget {
  const BatchCartTray({required this.controller, super.key});

  final AppController controller;

  @override
  State<BatchCartTray> createState() => _BatchCartTrayState();
}

class _BatchCartTrayState extends State<BatchCartTray> {
  bool _expanded = false;

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final cart = controller.batchCart;
    final theme = Theme.of(context);

    return Material(
      key: const Key('batch-cart-tray'),
      elevation: 8,
      color: theme.colorScheme.surfaceContainerHighest,
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
              child: Row(
                children: [
                  Icon(
                    Icons.shopping_cart_outlined,
                    color: theme.colorScheme.primary,
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: InkWell(
                      onTap: cart.isEmpty
                          ? null
                          : () => setState(() => _expanded = !_expanded),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              const Text(
                                'Toplu Taşıma Sepeti',
                                style: TextStyle(fontWeight: FontWeight.w800),
                              ),
                              const SizedBox(width: 6),
                              Container(
                                key: const Key('batch-cart-count'),
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 6,
                                  vertical: 2,
                                ),
                                decoration: BoxDecoration(
                                  color: theme.colorScheme.primary,
                                  borderRadius: BorderRadius.circular(10),
                                ),
                                child: Text(
                                  '${cart.length}',
                                  style: TextStyle(
                                    color: theme.colorScheme.onPrimary,
                                    fontSize: 12,
                                    fontWeight: FontWeight.w800,
                                  ),
                                ),
                              ),
                            ],
                          ),
                          Text(
                            cart.isEmpty
                                ? 'Parça ve kasa butonuna basarak ekleyin'
                                : '${cart.length} kasa hazır · Detaylar için dokunun',
                            style: TextStyle(
                              fontSize: 11,
                              color: theme.hintColor,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                  if (cart.isNotEmpty) ...[
                    TextButton(
                      key: const Key('batch-clear-cart'),
                      onPressed: controller.busy
                          ? null
                          : controller.exitBatchSelection,
                      child: const Text('Temizle'),
                    ),
                    const SizedBox(width: 4),
                    FilledButton.icon(
                      key: const Key('batch-submit'),
                      onPressed: controller.canReviewBatch && !controller.busy
                          ? controller.submitBatch
                          : null,
                      icon: const Icon(Icons.send, size: 16),
                      label: const Text('Tümünü Gönder'),
                    ),
                  ],
                ],
              ),
            ),
            if (_expanded && cart.isNotEmpty)
              Container(
                constraints: const BoxConstraints(maxHeight: 180),
                child: ListView.separated(
                  shrinkWrap: true,
                  padding: const EdgeInsets.fromLTRB(14, 0, 14, 10),
                  itemCount: cart.length,
                  separatorBuilder: (_, _) => const Divider(height: 1),
                  itemBuilder: (context, index) {
                    final item = cart[index];
                    final actionText = item.actionCode == 'FULL'
                        ? 'Dolu Kasa'
                        : 'Boş Kasa';
                    return ListTile(
                      dense: true,
                      contentPadding: EdgeInsets.zero,
                      title: Text(
                        '${item.effectivePart.partCode} · ${item.slotDisplayName}',
                        style: const TextStyle(fontWeight: FontWeight.w700),
                      ),
                      subtitle: Text(actionText),
                      trailing: IconButton(
                        key: Key('batch-remove-item-${item.cartItemId}'),
                        tooltip: 'Sepetten Çıkar',
                        icon: const Icon(
                          Icons.close,
                          size: 18,
                          color: Colors.redAccent,
                        ),
                        onPressed: () =>
                            controller.removePartFromBatch(item.cartItemId),
                      ),
                    );
                  },
                ),
              ),
          ],
        ),
      ),
    );
  }
}
