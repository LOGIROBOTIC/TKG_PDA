import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../data/models/pda_models.dart';
import '../../shared/presentation.dart';
import '../../shared/widgets/feedback_banners.dart';

/// Customer UI — Warehouse Preparation Console: a warehouse/supervisor-only
/// view of WAITING_PREPARATION transport requests, kept separate from the
/// line-operator part-selection flow (its own stage/route; no shared state
/// with machine selection or part-first browsing).
class WarehousePreparationScreen extends StatelessWidget {
  const WarehousePreparationScreen({required this.controller, super.key});

  final AppController controller;

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: controller,
    builder: (context, _) => Scaffold(
      appBar: AppBar(
        leading: IconButton(
          key: const Key('leave-warehouse-preparation'),
          tooltip: 'Makine listesi',
          onPressed: controller.busy
              ? null
              : controller.leaveWarehousePreparation,
          icon: const Icon(Icons.arrow_back),
        ),
        title: const Text('Depo Hazırlık'),
        actions: [
          IconButton(
            tooltip: 'Yenile',
            onPressed: controller.warehousePreparationLoading
                ? null
                : controller.loadWarehousePreparationRequests,
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
            Expanded(child: _Body(controller: controller)),
          ],
        ),
      ),
    ),
  );
}

class _Body extends StatelessWidget {
  const _Body({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final requests = controller.warehousePreparationRequests;
    if (controller.warehousePreparationLoading && requests.isEmpty) {
      return const Center(
        key: Key('warehouse-preparation-loading'),
        child: CircularProgressIndicator(),
      );
    }
    if (controller.warehousePreparationError case final error?) {
      if (requests.isEmpty) {
        return Padding(
          padding: const EdgeInsets.all(16),
          child: Card(
            key: const Key('warehouse-preparation-error'),
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(error),
                  const SizedBox(height: 10),
                  OutlinedButton.icon(
                    key: const Key('warehouse-preparation-retry'),
                    onPressed: controller.loadWarehousePreparationRequests,
                    icon: const Icon(Icons.refresh),
                    label: const Text('Yeniden Dene'),
                  ),
                ],
              ),
            ),
          ),
        );
      }
    }
    if (requests.isEmpty) {
      return const Center(
        key: Key('warehouse-preparation-empty'),
        child: Padding(
          padding: EdgeInsets.all(24),
          child: Text(
            'Hazırlık bekleyen talep yok.',
            style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700),
            textAlign: TextAlign.center,
          ),
        ),
      );
    }
    return RefreshIndicator(
      onRefresh: controller.loadWarehousePreparationRequests,
      child: ListView.separated(
        key: const Key('warehouse-preparation-list'),
        padding: const EdgeInsets.all(14),
        itemCount: requests.length,
        separatorBuilder: (_, _) => const SizedBox(height: 10),
        itemBuilder: (context, index) =>
            _RequestCard(request: requests[index], controller: controller),
      ),
    );
  }
}

class _RequestCard extends StatelessWidget {
  const _RequestCard({required this.request, required this.controller});

  final WarehousePreparationRequest request;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final marking = controller.isMarkingReady(request.requestId);
    return Card(
      key: Key('warehouse-item-${request.requestId}'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${request.machineCode} · ${knownOr(request.machineDisplayName)}',
              style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w900),
            ),
            const SizedBox(height: 4),
            Text(
              actionLabel(request.operation),
              style: const TextStyle(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            _InfoRow('Parça', knownOr(request.partCode)),
            if (request.sourceSlotDisplayName != null)
              _InfoRow(
                'Kaynak',
                operatorLocationLabel(
                  request.sourceSlotDisplayName,
                  request.sourceSlotCode,
                ),
              ),
            if (request.destinationSlotDisplayName != null)
              _InfoRow(
                'Hedef',
                operatorLocationLabel(
                  request.destinationSlotDisplayName,
                  request.destinationSlotCode,
                ),
              ),
            _InfoRow('Talep No', '#${request.requestNo}'),
            const SizedBox(height: 12),
            Align(
              alignment: Alignment.centerRight,
              child: ElevatedButton.icon(
                key: Key('warehouse-ready-${request.requestId}'),
                onPressed: marking ? null : () => _confirmReady(context),
                icon: marking
                    ? const SizedBox.square(
                        dimension: 18,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.check_circle_outline),
                label: const Text('Hazır'),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _confirmReady(BuildContext context) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Hazır olarak işaretle'),
        content: Text(
          '${request.machineCode} için ${knownOr(request.partCode)} malzemesi '
          'hazırlandı mı?',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Vazgeç'),
          ),
          ElevatedButton(
            key: const Key('confirm-ready'),
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Onayla'),
          ),
        ],
      ),
    );
    if (confirmed == true) {
      await controller.markPreparationReady(request.requestId);
    }
  }
}

class _InfoRow extends StatelessWidget {
  const _InfoRow(this.label, this.value);

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 2),
    child: Text.rich(
      TextSpan(
        children: [
          TextSpan(
            text: '$label: ',
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
          TextSpan(text: value),
        ],
      ),
    ),
  );
}
