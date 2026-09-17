import 'package:flutter/material.dart';

import '../../app/app_controller.dart';
import '../../app/design_tokens.dart';

/// The warehouse tablet's home: the two halves of the job. Semi-finished
/// preparation feeds machines; the finished-goods store receives what they
/// produce.
///
/// Both halves are live. Which physical locations the finished-goods store has
/// is still a customer decision, so that screen may legitimately show none.
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
                      'Robotun getirdiği bitmiş ürünleri görün ve aldığınız alanları bildirin.',
                  color: PdaStatusColors.done,
                  onPressed: controller.enterFinishedGoodsStore,
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
