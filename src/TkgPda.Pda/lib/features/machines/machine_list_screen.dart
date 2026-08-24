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
        title: const Text('Makine Seçimi'),
        actions: [
          IconButton(
            tooltip: 'Yenile',
            onPressed: controller.busy ? null : controller.reloadMachines,
            icon: const Icon(Icons.refresh),
          ),
          TextButton.icon(
            key: const Key('logout-button'),
            onPressed: controller.logout,
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
                  const SizedBox(height: 8),
                  Wrap(
                    spacing: 8,
                    children: [
                      _PermissionChip(
                        icon: Icons.visibility,
                        label: 'Görüntüleme',
                        enabled: machine.permissions.canView,
                      ),
                      _PermissionChip(
                        icon: Icons.local_shipping,
                        label: 'Operasyon',
                        enabled: machine.permissions.canOperate,
                      ),
                      if (machine.permissions.canChangeVariant)
                        const _PermissionChip(
                          icon: Icons.swap_horiz,
                          label: 'Varyant',
                          enabled: true,
                        ),
                    ],
                  ),
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

class _PermissionChip extends StatelessWidget {
  const _PermissionChip({
    required this.icon,
    required this.label,
    required this.enabled,
  });

  final IconData icon;
  final String label;
  final bool enabled;

  @override
  Widget build(BuildContext context) => Chip(
    avatar: Icon(icon, size: 18),
    label: Text(label),
    backgroundColor: enabled ? const Color(0xFFDCFCE7) : Colors.grey.shade200,
  );
}
