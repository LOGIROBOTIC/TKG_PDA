import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../app/design_tokens.dart';

/// The warehouse tablet's home: the two halves of the job. Semi-finished
/// preparation feeds machines; the finished-goods store receives what they
/// produce.
///
/// Only the first half has a backend. The second is shown as not yet available
/// rather than opening a screen that cannot report anything real.
class WarehouseHomeScreen extends StatelessWidget {
  const WarehouseHomeScreen({required this.controller, super.key});

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
            const Text('Depo'),
            if (controller.user case final user?)
              Text(
                '${user.fullName.trim().isNotEmpty ? user.fullName : user.username}'
                ' (Sicil: ${user.employeeNo?.trim().isNotEmpty == true ? user.employeeNo! : "—"})',
                key: const Key('warehouse-user-banner'),
                style: const TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.normal,
                ),
                overflow: TextOverflow.ellipsis,
              ),
          ],
        ),
        actions: [
          IconButton(
            key: const Key('logout-button'),
            tooltip: 'Çıkış',
            onPressed: controller.isMutationInFlight ? null : controller.logout,
            icon: const Icon(Icons.logout),
          ),
        ],
      ),
      body: SafeArea(
        child: Padding(
          padding: EdgeInsets.all(PdaScale.gutter(context)),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Expanded(
                child: _HomeTile(
                  actionKey: const Key('open-preparation-areas'),
                  icon: Icons.inventory_2_outlined,
                  title: 'Yarı Mamul Hazırlık',
                  description:
                      'Makinelerden gelen talepleri dört hazırlık alanında hazırlayın.',
                  color: PdaStatusColors.running,
                  onPressed: controller.enterPreparationAreas,
                ),
              ),
              SizedBox(height: PdaScale.gutter(context)),
              Expanded(
                child: _HomeTile(
                  actionKey: const Key('open-finished-goods'),
                  icon: Icons.warehouse_outlined,
                  title: 'Bitmiş Ürün Deposu',
                  description:
                      'Robotun getirdiği bitmiş ürünler. Bu bölüm henüz hazır değil.',
                  color: PdaStatusColors.unknown,
                  // Deliberately inert: no backend models the finished-goods
                  // side yet, so an enabled button would promise something that
                  // cannot be reported.
                  onPressed: null,
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

class _HomeTile extends StatelessWidget {
  const _HomeTile({
    required this.actionKey,
    required this.icon,
    required this.title,
    required this.description,
    required this.color,
    required this.onPressed,
  });

  final Key actionKey;
  final IconData icon;
  final String title;
  final String description;
  final Color color;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final enabled = onPressed != null;
    return Material(
      key: actionKey,
      color: color.withValues(alpha: enabled ? 0.14 : 0.07),
      borderRadius: BorderRadius.circular(PdaScale.actionRadius),
      child: InkWell(
        onTap: onPressed,
        borderRadius: BorderRadius.circular(PdaScale.actionRadius),
        child: Padding(
          padding: EdgeInsets.all(PdaScale.cardPadding(context)),
          child: Row(
            children: [
              Icon(icon, size: 48, color: enabled ? color : Colors.grey),
              SizedBox(width: PdaScale.gutter(context)),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Text(
                      title,
                      style: TextStyle(
                        fontSize: PdaScale.title(context),
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    SizedBox(height: PdaScale.gutter(context) / 3),
                    Text(
                      description,
                      style: TextStyle(fontSize: PdaScale.body(context)),
                    ),
                  ],
                ),
              ),
              if (enabled) const Icon(Icons.chevron_right),
            ],
          ),
        ),
      ),
    );
  }
}
