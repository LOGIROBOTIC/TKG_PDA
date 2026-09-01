import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../data/models/pda_models.dart';
import '../../shared/presentation.dart';

/// Stable PDA action surface. It remains reachable after the effective part
/// and compatible variant are resolved; the backend still owns every physical
/// source/destination decision.
class OperatorTransportActions extends StatelessWidget {
  const OperatorTransportActions({required this.controller, super.key});

  final AppController controller;

  static const _operations = ['BRING_EMPTY', 'REMOVE_FULL', 'REMOVE_EMPTY'];

  @override
  Widget build(BuildContext context) {
    if (!controller.transportActionsReady) {
      return const SizedBox.shrink();
    }
    return Material(
      key: const Key('direct-actions'),
      elevation: 10,
      color: Colors.white,
      child: SafeArea(top: false, child: _ActionArea(controller: controller)),
    );
  }
}

class _ActionArea extends StatelessWidget {
  const _ActionArea({required this.controller});

  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final canOperate =
        controller.selectedMachine?.permissions.canOperate ?? false;
    if (!canOperate) return const SizedBox.shrink();

    final effectivePart = controller.selectedEffectivePart!;
    final variant = controller.resolvedCompatibleVariant!;
    final machine = controller.selectedMachine!;
    final submitting = controller.submittingOperation;
    return Padding(
      padding: const EdgeInsets.fromLTRB(10, 8, 10, 8),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            '${machine.code} · ${effectivePart.partCode} · ${variant.variantCode}',
            key: const Key('operation-effective-part'),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(fontWeight: FontWeight.w800),
          ),
          if (submitting != null) ...[
            const SizedBox(height: 4),
            const Row(
              key: Key('transport-submitting'),
              children: [
                SizedBox.square(
                  dimension: 16,
                  child: CircularProgressIndicator(strokeWidth: 2.5),
                ),
                SizedBox(width: 8),
                Text(
                  'Talep gönderiliyor...',
                  style: TextStyle(fontWeight: FontWeight.w700),
                ),
              ],
            ),
          ],
          const SizedBox(height: 8),
          SizedBox(
            height: 82,
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                for (final operation in OperatorTransportActions._operations)
                  Expanded(
                    child: Padding(
                      padding: EdgeInsets.only(
                        right:
                            operation ==
                                OperatorTransportActions._operations.last
                            ? 0
                            : 6,
                      ),
                      child: _OperationButton(
                        operation: operation,
                        controller: controller,
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ],
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
    return ElevatedButton(
      key: Key('direct-action-$operation'),
      style: ElevatedButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 7),
        textStyle: const TextStyle(fontSize: 13, fontWeight: FontWeight.w800),
      ),
      onPressed: enabled ? () => _confirm(context) : null,
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(actionIcon(operation), size: 24),
          const SizedBox(height: 3),
          Text(
            actionLabel(operation),
            maxLines: 2,
            textAlign: TextAlign.center,
            overflow: TextOverflow.ellipsis,
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
              Text(
                actionQuestion(operation),
                key: const Key('operation-confirmation-question'),
                style: const TextStyle(
                  fontSize: 22,
                  fontWeight: FontWeight.w900,
                ),
              ),
              const SizedBox(height: 12),
              Text(
                '$machineCode · $partCode · $variantCode',
                key: const Key('operation-confirmation-context'),
                style: const TextStyle(
                  color: Color(0xFF475569),
                  fontWeight: FontWeight.w700,
                ),
              ),
              const SizedBox(height: 20),
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      onPressed: () => Navigator.pop(context, false),
                      child: const Text('Vazgeç'),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: ElevatedButton.icon(
                      key: const Key('confirm-direct-transport'),
                      onPressed: () => Navigator.pop(context, true),
                      icon: Icon(actionIcon(operation)),
                      label: Text(
                        actionLabel(operation),
                        textAlign: TextAlign.center,
                      ),
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

class OperatorActiveTransportList extends StatelessWidget {
  const OperatorActiveTransportList({
    required this.requests,
    required this.controller,
    super.key,
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
            if (execution.failureCode != null)
              Text(
                failureLabel(execution.failureCode, execution.operatorMessage),
                key: const Key('operator-failure-message'),
                style: const TextStyle(fontWeight: FontWeight.w800),
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
