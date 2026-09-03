import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../data/models/pda_models.dart';
import '../../shared/presentation.dart';

/// Existing active-request inspection and cancellation surface. WP-33 keeps
/// cancellation confirmation while the legacy creation confirmation is gone.
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
