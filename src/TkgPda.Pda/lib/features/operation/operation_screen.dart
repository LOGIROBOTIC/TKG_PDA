import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../data/models/pda_models.dart';
import '../../shared/presentation.dart';
import '../../shared/widgets/feedback_banners.dart';
import 'layout_canvas.dart';

class OperationScreen extends StatelessWidget {
  const OperationScreen({required this.controller, super.key});

  final AppController controller;

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: controller,
    builder: (context, _) {
      final operational = controller.context;
      return Scaffold(
        appBar: AppBar(
          leading: IconButton(
            key: const Key('leave-machine'),
            tooltip: 'Makine listesi',
            onPressed: controller.busy ? null : controller.leaveMachine,
            icon: const Icon(Icons.arrow_back),
          ),
          title: Text(
            controller.selectedMachine == null
                ? 'Makine Operasyonu'
                : '${controller.selectedMachine!.code} · Operasyon',
          ),
          actions: [
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
              if (controller.hasAmbiguousSubmission)
                _AmbiguousSubmissionBanner(controller: controller),
              Expanded(
                child: operational == null
                    ? const Center(child: CircularProgressIndicator())
                    : RefreshIndicator(
                        onRefresh: () =>
                            controller.refreshContext(showBusy: true),
                        child: ListView(
                          key: const Key('operation-content'),
                          padding: const EdgeInsets.all(14),
                          children: [
                            _MachineHeader(
                              contextData: operational,
                              controller: controller,
                            ),
                            const SizedBox(height: 14),
                            _ProductSection(
                              contextData: operational,
                              controller: controller,
                            ),
                            const SizedBox(height: 14),
                            Card(
                              child: Padding(
                                padding: const EdgeInsets.all(14),
                                child: LayoutCanvas(
                                  slots: operational.slots,
                                  selectedSlotId: controller.selectedSlot?.id,
                                  onSlotSelected: controller.selectSlot,
                                ),
                              ),
                            ),
                            if (controller.selectedSlot case final slot?) ...[
                              const SizedBox(height: 14),
                              _SlotDetails(slot: slot, controller: controller),
                            ],
                            const SizedBox(height: 14),
                            _ActiveTransportSection(
                              requests: operational.activeTransportRequests,
                              controller: controller,
                            ),
                            const SizedBox(height: 40),
                          ],
                        ),
                      ),
              ),
              if (controller.busy) const LinearProgressIndicator(minHeight: 5),
            ],
          ),
        ),
      );
    },
  );
}

class _MachineHeader extends StatelessWidget {
  const _MachineHeader({required this.contextData, required this.controller});

  final OperationalContext contextData;
  final AppController controller;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Row(
        children: [
          const CircleAvatar(
            radius: 30,
            child: Icon(Icons.precision_manufacturing, size: 34),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  contextData.machine.displayName,
                  style: const TextStyle(
                    fontSize: 19,
                    fontWeight: FontWeight.w800,
                  ),
                ),
                Text(
                  'Aktif Varyant: ${knownOr(contextData.activeVariantCode)} · ${knownOr(contextData.activeVariantName)}',
                  key: const Key('active-variant'),
                  style: const TextStyle(
                    fontSize: 18,
                    fontWeight: FontWeight.w900,
                  ),
                ),
                Text('Layout: ${knownOr(contextData.activeLayoutName)}'),
              ],
            ),
          ),
          if (contextData.machine.permissions.canChangeVariant)
            OutlinedButton.icon(
              key: const Key('variant-selection-button'),
              onPressed: controller.mutationEnabled
                  ? () => _selectVariant(context)
                  : null,
              icon: const Icon(Icons.swap_horiz),
              label: const Text('Varyant'),
            ),
        ],
      ),
    ),
  );

  Future<void> _selectVariant(BuildContext context) async {
    final option = await showDialog<VariantLayoutOption>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Üretim varyantını değiştir'),
        content: SizedBox(
          width: 520,
          child: ListView.separated(
            shrinkWrap: true,
            itemCount: contextData.variantLayouts.length,
            separatorBuilder: (_, _) => const Divider(),
            itemBuilder: (context, index) {
              final item = contextData.variantLayouts[index];
              return ListTile(
                key: Key('variant-${item.variantId}-${item.layoutId}'),
                title: Text('${item.variantCode} · ${item.variantName}'),
                subtitle: Text('Layout: ${item.layoutName}'),
                trailing:
                    item.variantId == contextData.activeVariantId &&
                        item.layoutId == contextData.activeLayoutId
                    ? const Icon(Icons.check_circle, color: Colors.green)
                    : null,
                onTap: () => Navigator.pop(context, item),
              );
            },
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('Vazgeç'),
          ),
        ],
      ),
    );
    if (option == null || !context.mounted) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Varyant değişikliğini onaylayın'),
        content: Text(
          '${option.variantCode} · ${option.variantName}\n${option.layoutName}\n\nAktif ekran yeniden yüklenecektir.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('İptal'),
          ),
          ElevatedButton(
            key: const Key('confirm-variant-switch'),
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Değiştir'),
          ),
        ],
      ),
    );
    if (confirmed == true) {
      await controller.switchVariant(option);
    }
  }
}

class _ProductSection extends StatelessWidget {
  const _ProductSection({required this.contextData, required this.controller});

  final OperationalContext contextData;
  final AppController controller;

  @override
  Widget build(BuildContext context) => Card(
    child: Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Parça / Alt Parça',
            style: TextStyle(fontSize: 20, fontWeight: FontWeight.w900),
          ),
          const SizedBox(height: 10),
          if (contextData.productGroups.isEmpty)
            const Text('Aktif varyant için parça grubu tanımlı değil.')
          else
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final group in contextData.productGroups)
                  ChoiceChip(
                    key: Key('product-group-${group.id}'),
                    selected: controller.selectedProductGroup?.id == group.id,
                    label: Text(
                      '${group.mainPartCode} · ${knownOr(group.name)}',
                    ),
                    onSelected: (selected) =>
                        controller.selectProductGroup(selected ? group : null),
                  ),
              ],
            ),
          if (controller.selectedProductGroup case final group?) ...[
            const Divider(height: 28),
            Text(
              'Alt Parçalar · ${group.mainPartCode}',
              style: const TextStyle(fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 8),
            if (group.parts.isEmpty)
              const Text('Alt parça tanımlı değil.')
            else
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  for (final part in group.parts)
                    ChoiceChip(
                      key: Key('product-part-${part.id}'),
                      selected: controller.selectedPart?.id == part.id,
                      label: Text(
                        '${part.partCode}\nKasa: ${knownOr(part.containerCode)} · Lot: ${part.lotQuantity?.toStringAsFixed(0) ?? 'Tanımlı değil'}',
                      ),
                      onSelected: (selected) =>
                          controller.selectPart(selected ? part : null),
                    ),
                ],
              ),
          ],
        ],
      ),
    ),
  );
}

class _SlotDetails extends StatelessWidget {
  const _SlotDetails({required this.slot, required this.controller});

  final LayoutSlot slot;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final busy = slot.availableActions.any(
      (action) => action.disabledCode == 'SLOT_BUSY',
    );
    return Card(
      key: const Key('slot-details'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    '${slot.displayName} · ${slot.code}',
                    style: const TextStyle(
                      fontSize: 21,
                      fontWeight: FontWeight.w900,
                    ),
                  ),
                ),
                if (busy)
                  const Chip(
                    avatar: Icon(Icons.lock_clock, size: 18),
                    label: Text('Kullanımda'),
                    backgroundColor: Color(0xFFFED7AA),
                  ),
              ],
            ),
            const SizedBox(height: 10),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                Chip(label: Text(occupancyLabel(slot.occupancyState))),
                Chip(label: Text(loadLabel(slot.loadState))),
                Chip(label: Text('Parça: ${knownOr(slot.currentPartCode)}')),
                Chip(
                  label: Text(
                    'Kasa: ${knownOr(slot.currentContainerCode ?? slot.defaultContainerCode)}',
                  ),
                ),
              ],
            ),
            const SizedBox(height: 10),
            _RdsReadiness(slot: slot),
            if (slot.loadStateActions.isNotEmpty) ...[
              const Divider(height: 28),
              const Text(
                'Kasa Yük Durumu',
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w900),
              ),
              const SizedBox(height: 8),
              for (final action in slot.loadStateActions)
                _LoadStateActionRow(
                  slot: slot,
                  action: action,
                  controller: controller,
                ),
            ],
            const Divider(height: 28),
            const Text(
              'Kullanılabilir İşlemler',
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w900),
            ),
            const SizedBox(height: 8),
            for (final action in slot.availableActions)
              _ActionRow(slot: slot, action: action, controller: controller),
          ],
        ),
      ),
    );
  }
}

class _LoadStateActionRow extends StatelessWidget {
  const _LoadStateActionRow({
    required this.slot,
    required this.action,
    required this.controller,
  });

  final LayoutSlot slot;
  final AvailableAction action;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final enabled = action.enabled && controller.mutationEnabled;
    final label = action.action == 'MARK_FULL' ? 'Kasa Doldu' : 'Kasa Boş';
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Row(
        children: [
          Expanded(
            flex: 2,
            child: OutlinedButton(
              key: Key('load-state-${action.action}'),
              onPressed: enabled ? () => _confirm(context, label) : null,
              child: Text(label),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            flex: 3,
            child: Text(
              enabled
                  ? 'Sunucu bu durum bildirimine izin veriyor.'
                  : action.disabledMessage ??
                        action.disabledCode ??
                        'Kullanılamıyor.',
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _confirm(BuildContext context, String label) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('$label olarak işaretle'),
        content: Text(
          '${slot.displayName} için yük durumu değiştirilsin mi? Bu işlem taşıma talebi oluşturmaz.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Vazgeç'),
          ),
          ElevatedButton(
            key: Key('confirm-${action.action}'),
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Onayla'),
          ),
        ],
      ),
    );
    if (confirmed == true) await controller.reportLoadState(slot, action);
  }
}

class _RdsReadiness extends StatelessWidget {
  const _RdsReadiness({required this.slot});

  final LayoutSlot slot;

  @override
  Widget build(BuildContext context) {
    final messages = <String>[];
    if (!slot.executionReadiness.isExecutionReady) {
      messages.add(readinessLabel(slot.executionReadiness.readinessCode));
    }
    if (messages.isEmpty && slot.executionReadiness.isExecutionReady) {
      messages.add('Robot noktası eşleme bilgisi hazır.');
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (final message in messages)
          Padding(
            padding: const EdgeInsets.only(bottom: 4),
            child: Row(
              children: [
                Icon(
                  messages.length == 1 &&
                          slot.executionReadiness.isExecutionReady
                      ? Icons.check_circle
                      : Icons.info_outline,
                  size: 18,
                ),
                const SizedBox(width: 8),
                Expanded(child: Text(message)),
              ],
            ),
          ),
      ],
    );
  }
}

class _ActionRow extends StatelessWidget {
  const _ActionRow({
    required this.slot,
    required this.action,
    required this.controller,
  });

  final LayoutSlot slot;
  final AvailableAction action;
  final AppController controller;

  @override
  Widget build(BuildContext context) {
    final enabled = action.enabled && controller.mutationEnabled;
    final disabledMessage = !action.enabled
        ? action.disabledMessage ?? action.disabledCode
        : controller.offline
        ? 'Bağlantı yokken işlem yapılamaz.'
        : null;
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          Expanded(
            flex: 2,
            child: ElevatedButton(
              key: Key('action-${action.action}'),
              onPressed: enabled ? () => _confirm(context) : null,
              child: Text(actionLabel(action.action)),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            flex: 3,
            child: Text(
              enabled
                  ? 'Sunucu işlemi uygun buldu.'
                  : disabledMessage ?? 'Kullanılamıyor.',
              key: Key('action-reason-${action.action}'),
              style: TextStyle(
                color: enabled
                    ? const Color(0xFF166534)
                    : const Color(0xFF7F1D1D),
                fontWeight: FontWeight.w700,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _confirm(BuildContext context) async {
    final command = controller.commandFor(slot, action);
    final group = controller.selectedProductGroup;
    final part = controller.selectedPart;
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
              _ConfirmationLine('Makine', controller.context!.machine.code),
              _ConfirmationLine(
                'Varyant',
                knownOr(controller.context!.activeVariantCode),
              ),
              _ConfirmationLine(
                'Parça',
                group?.mainPartCode ?? 'Tanımlı değil',
              ),
              _ConfirmationLine('Alt Parça', part?.partCode ?? 'Tanımlı değil'),
              _ConfirmationLine('Konum', '${slot.displayName} · ${slot.code}'),
              _ConfirmationLine('İşlem', actionLabel(action.action)),
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
                      key: const Key('confirm-transport'),
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
    if (confirmed == true) {
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
              Text(
                'Aktif Taşımalar (${requests.length})',
                style: const TextStyle(
                  fontSize: 20,
                  fontWeight: FontWeight.w900,
                ),
              ),
            ],
          ),
          const SizedBox(height: 10),
          if (requests.isEmpty)
            const Text('Bu makine için aktif taşıma talebi yok.')
          else
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
    if (confirmed == true) {
      await controller.cancelTransport(request);
    }
  }
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
