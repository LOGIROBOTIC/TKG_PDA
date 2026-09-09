import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../data/models/pda_models.dart';
import '../../shared/widgets/feedback_banners.dart';

class MachineListScreen extends StatelessWidget {
  const MachineListScreen({required this.controller, super.key});

  final AppController controller;

  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: controller,
    builder: (context, _) => Scaffold(
      appBar: AppBar(
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            const Text('Makine Seçimi'),
            if (controller.user case final user?)
              Text(
                '${user.fullName.trim().isNotEmpty ? user.fullName : user.username} (Sicil: ${user.employeeNo?.trim().isNotEmpty == true ? user.employeeNo! : "—"})',
                key: const Key('operator-user-banner'),
                style: const TextStyle(fontSize: 12, fontWeight: FontWeight.normal),
                overflow: TextOverflow.ellipsis,
              ),
          ],
        ),
        actions: [
          IconButton(
            key: const Key('open-operator-queue'),
            tooltip: 'Görevler',
            onPressed: controller.busy ? null : controller.enterTasks,
            icon: const Icon(Icons.format_list_numbered),
          ),
          if (controller.canAccessWarehousePreparation)
            IconButton(
              key: const Key('open-warehouse-preparation'),
              tooltip: 'Depo Hazırlık',
              onPressed: controller.busy
                  ? null
                  : controller.enterWarehousePreparation,
              icon: const Icon(Icons.inventory_2_outlined),
            ),
          IconButton(
            tooltip: 'Yenile',
            onPressed: controller.busy ? null : controller.reloadMachines,
            icon: const Icon(Icons.refresh),
          ),
          TextButton.icon(
            key: const Key('logout-button'),
            onPressed: controller.isMutationInFlight ? null : controller.logout,
            icon: const Icon(Icons.logout, color: Colors.white),
            label: const Text('Çıkış', style: TextStyle(color: Colors.white)),
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
            Expanded(
              child: controller.busy && controller.machines.isEmpty
                  ? const Center(child: CircularProgressIndicator())
                  : controller.machines.isEmpty
                  ? RefreshIndicator(
                      onRefresh: controller.reloadMachines,
                      child: ListView(
                        key: const Key('machine-list-empty'),
                        padding: const EdgeInsets.all(24),
                        children: const [
                          SizedBox(height: 60),
                          Icon(
                            Icons.precision_manufacturing_outlined,
                            size: 56,
                            color: Colors.grey,
                          ),
                          SizedBox(height: 16),
                          Text(
                            'Size tanımlı makine bulunamadı.',
                            textAlign: TextAlign.center,
                            style: TextStyle(
                              fontSize: 17,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                          SizedBox(height: 8),
                          Text(
                            'Erişim gerekiyorsa yöneticinizle iletişime geçin.',
                            textAlign: TextAlign.center,
                          ),
                        ],
                      ),
                    )
                  : RefreshIndicator(
                      onRefresh: controller.reloadMachines,
                      child: ListView.separated(
                        key: const Key('machine-list'),
                        padding: const EdgeInsets.all(16),
                        itemCount: controller.machines.length,
                        separatorBuilder: (_, _) => const SizedBox(height: 12),
                        itemBuilder: (context, index) => _MachineCard(
                          machine: controller.machines[index],
                          enabled: !controller.busy,
                          onTap: controller.selectMachine,
                        ),
                      ),
                    ),
            ),
          ],
        ),
      ),
    ),
  );
}

class _MachineCard extends StatelessWidget {
  const _MachineCard({
    required this.machine,
    required this.enabled,
    required this.onTap,
  });

  final MachineSummary machine;
  final bool enabled;
  final ValueChanged<MachineSummary> onTap;

  @override
  Widget build(BuildContext context) => Card(
    key: Key('machine-${machine.id}'),
    child: InkWell(
      onTap: enabled ? () => onTap(machine) : null,
      borderRadius: BorderRadius.circular(12),
      child: Padding(
        padding: const EdgeInsets.all(18),
        child: Row(
          children: [
            CircleAvatar(
              radius: 30,
              backgroundColor: machine.permissions.canOperate
                  ? Theme.of(context).colorScheme.primaryContainer
                  : Colors.grey.shade300,
              child: const Icon(Icons.precision_manufacturing, size: 32),
            ),
            const SizedBox(width: 16),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    machine.code,
                    style: const TextStyle(
                      fontSize: 21,
                      fontWeight: FontWeight.w900,
                    ),
                  ),
                  Text(
                    machine.displayName,
                    style: const TextStyle(fontSize: 16),
                  ),
                  if (!machine.permissions.canOperate) ...[
                    const SizedBox(height: 8),
                    Text(
                      'Bu makinede taşıma işlemi yapamazsınız.',
                      key: Key('machine-unavailable-reason-${machine.id}'),
                      style: const TextStyle(
                        color: Color(0xFF9A3412),
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ],
                ],
              ),
            ),
            const Icon(Icons.chevron_right, size: 36),
          ],
        ),
      ),
    ),
  );
}
