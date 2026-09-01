import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../data/models/pda_models.dart';
import '../../shared/presentation.dart';

/// Final Operator action surface. The Operator chooses only the business
/// operation; the backend resolves every physical source/destination slot.
class OperatorTransportPanel extends StatelessWidget {
  const OperatorTransportPanel({required this.controller, super.key});

  final AppController controller;

  static const _operations = ['BRING_EMPTY', 'REMOVE_FULL', 'REMOVE_EMPTY'];

  @override
  Widget build(BuildContext context) {
    if (!controller.transportActionsReady) {
      return const SizedBox.shrink();
    }
    final requests = controller.context?.activeTransportRequests ?? const [];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _ActionCard(controller: controller),
        if (requests.isNotEmpty) ...[
          const SizedBox(height: 14),
          _ActiveTransportSection(requests: requests, controller: controller),
        ],
      ],
    );
  }
}

class _ActionCard extends StatelessWidget {
  const _ActionCard({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final canOperate =
        controller.selectedMachine?.permissions.canOperate ?? false;
    if (!canOperate) return const SizedBox.shrink();

    final effectivePart = controller.selectedEffectivePart!;
    final mainPart = controller.selectedMainPart;
    return Card(
      key: const Key('direct-actions'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Taşıma İşlemi',
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w900),
            ),
            const SizedBox(height: 4),
            Text(
              effectivePart.isMain || mainPart == null
                  ? 'Parça: ${effectivePart.partCode}'
                  : 'Ana parça: ${mainPart.partCode} · İşlem parçası: ${effectivePart.partCode}',
              key: const Key('operation-effective-part'),
              style: const TextStyle(fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 4),
            const Text('Fiziksel konumu sistem otomatik seçecek.'),
            const SizedBox(height: 12),
            for (final operation in OperatorTransportPanel._operations)
              _OperationButton(operation: operation, controller: controller),
          ],
        ),
      ),
    );
  }
}

class _OperationButton extends StatelessWidget {
  const _OperationButton({required this.operation, required this.controller});

  final String operation;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final inProgress = controller.operationInProgress(operation);
    final enabled = controller.mutationEnabled && !inProgress;
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            width: double.infinity,
            child: ElevatedButton(
              key: Key('direct-action-$operation'),
              onPressed: enabled ? () => _confirm(context) : null,
              child: Text(actionLabel(operation)),
            ),
          ),
          if (inProgress)
            const Padding(
              padding: EdgeInsets.only(top: 4),
              child: Text(
                'Bu işlem için aktif bir taşıma talebi var.',
                style: TextStyle(fontSize: 12, color: Color(0xFF475569)),
              ),
            ),
        ],
      ),
    );
  }

  Future<void> _confirm(BuildContext context) async {
    final TransportCommand command;
    try {
      command = controller.commandForDirect(operation);
    } on StateError {
      return;
    }
    final machineCode = controller.context!.machine.code;
    final variantCode = controller.resolvedCompatibleVariant!.variantCode;
    final partCode = controller.selectedEffectivePart!.partCode;
    final confirmed = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (context) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(22),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text(
                'Taşıma Talebini Onaylayın',
                style: TextStyle(fontSize: 23, fontWeight: FontWeight.w900),
              ),
              const SizedBox(height: 16),
              _ConfirmationLine('Makine', machineCode),
              _ConfirmationLine('Varyant', variantCode),
              _ConfirmationLine('Parça', partCode),
              _ConfirmationLine('Konum', 'Sistem tarafından seçilecek'),
              _ConfirmationLine('İşlem', actionLabel(operation)),
              const SizedBox(height: 20),
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      onPressed: () => Navigator.pop(context, false),
                      child: const Text('İptal'),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: ElevatedButton(
                      key: const Key('confirm-direct-transport'),
                      onPressed: () => Navigator.pop(context, true),
                      child: const Text('Onayla'),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
    if (confirmed != true) return;
    if (controller.isConfiguringBatchItem) {
      await controller.confirmBatchItemCommand(command, operation: operation);
    } else {
      await controller.submitTransport(command);
    }
  }
}

class _ConfirmationLine extends StatelessWidget {
  const _ConfirmationLine(this.label, this.value);

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 4),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 100,
          child: Text(
            label,
            style: const TextStyle(fontWeight: FontWeight.w800),
          ),
        ),
        Expanded(child: Text(value)),
      ],
    ),
  );
}

class _ActiveTransportSection extends StatelessWidget {
  const _ActiveTransportSection({
    required this.requests,
    required this.controller,
  });

  final List<ActiveTransportRequest> requests;
  final AppController controller;

  @override
  Widget build(BuildContext context) => Card(
    key: const Key('active-transport-section'),
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(Icons.local_shipping, size: 30),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  'Aktif Taşımalar (${requests.length})',
                  style: const TextStyle(
                    fontSize: 20,
                    fontWeight: FontWeight.w900,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          for (final request in requests) ...[
            _TransportCard(request: request, controller: controller),
            const SizedBox(height: 10),
          ],
        ],
      ),
    ),
  );
}

class _TransportCard extends StatelessWidget {
  const _TransportCard({required this.request, required this.controller});

  final ActiveTransportRequest request;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final execution = request.execution;
    final failure = execution.failureCode != null;
    return Material(
      key: Key('active-transport-${request.requestId}'),
      color: failure ? const Color(0xFFFEE2E2) : const Color(0xFFEFF6FF),
      shape: RoundedRectangleBorder(
        side: BorderSide(
          color: failure ? const Color(0xFFB91C1C) : const Color(0xFF3B82F6),
        ),
        borderRadius: BorderRadius.circular(10),
      ),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    '#${request.requestNo} · ${actionLabel(request.operation)}',
                    style: const TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.w900,
                    ),
                  ),
                ),
                Chip(label: Text(executionStateLabel(execution.state))),
              ],
            ),
            if (execution.agvId != null) Text('Atanan AGV: ${execution.agvId}'),
            if (execution.failureCode != null)
              Text(
                failureLabel(execution.failureCode, execution.operatorMessage),
                key: const Key('operator-failure-message'),
                style: const TextStyle(fontWeight: FontWeight.w800),
              ),
            if (execution.correlationId.isNotEmpty)
              ExpansionTile(
                tilePadding: EdgeInsets.zero,
                title: const Text('Destek referansı'),
                children: [
                  SelectableText(execution.correlationId),
                  if (execution.technicalMessage != null)
                    SelectableText(
                      'Teknik ayrıntı: ${execution.technicalMessage}',
                    ),
                ],
              ),
            if (request.canCancel)
              OutlinedButton.icon(
                key: Key('cancel-${request.requestId}'),
                style: OutlinedButton.styleFrom(
                  foregroundColor: Theme.of(context).colorScheme.error,
                ),
                onPressed: controller.mutationEnabled
                    ? () => _confirmCancel(context)
                    : null,
                icon: const Icon(Icons.cancel_outlined),
                label: const Text('Talebi İptal Et'),
              )
            else if (request.cancelDisabledMessage != null)
              Text(request.cancelDisabledMessage!),
          ],
        ),
      ),
    );
  }

  Future<void> _confirmCancel(BuildContext context) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Talebi iptal et'),
        content: Text(
          '#${request.requestNo} numaralı talep için iptal isteği gönderilsin mi?\n\nRobotun fiziksel olarak durduğu yalnız sunucu durumu ile doğrulanır.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Vazgeç'),
          ),
          ElevatedButton(
            key: const Key('confirm-cancel'),
            style: ElevatedButton.styleFrom(
              backgroundColor: Theme.of(context).colorScheme.error,
              foregroundColor: Colors.white,
            ),
            onPressed: () => Navigator.pop(context, true),
            child: const Text('İptal İsteği Gönder'),
          ),
        ],
      ),
    );
    if (confirmed == true) await controller.cancelTransport(request);
  }
}
