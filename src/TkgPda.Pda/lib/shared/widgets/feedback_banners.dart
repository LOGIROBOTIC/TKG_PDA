import 'package:flutter/material.dart';

import '../../core/errors/api_problem.dart';
import '../../data/models/pda_models.dart';

class OfflineBanner extends StatelessWidget {
  const OfflineBanner({super.key});

  @override
  Widget build(BuildContext context) => Container(
    key: const Key('offline-banner'),
    width: double.infinity,
    color: const Color(0xFF7C2D12),
    padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
    child: const Row(
      children: [
        Icon(Icons.cloud_off, color: Colors.white),
        SizedBox(width: 10),
        Expanded(
          child: Text(
            'Bağlantı yok — bilgiler güncel olmayabilir. Yeni işlem yapılamaz.',
            style: TextStyle(color: Colors.white, fontWeight: FontWeight.w700),
          ),
        ),
      ],
    ),
  );
}

class ProblemBanner extends StatelessWidget {
  const ProblemBanner({required this.problem, super.key});

  final ApiProblem problem;

  @override
  Widget build(BuildContext context) => Container(
    key: const Key('problem-banner'),
    width: double.infinity,
    color: Theme.of(context).colorScheme.errorContainer,
    padding: const EdgeInsets.all(12),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          problem.operatorMessage,
          style: const TextStyle(fontWeight: FontWeight.w700),
        ),
      ],
    ),
  );
}

/// Customer Monitoring — Robot Alarm Visibility. A coarse, non-technical
/// signal only: no robot identity or alarm code is ever shown here. `error`
/// (RDS-unavailable) and `summary` are rendered as distinct states — a
/// failed read must never look like "no active alarm".
class RobotAlarmBanner extends StatelessWidget {
  const RobotAlarmBanner({
    required this.summary,
    required this.error,
    required this.onRetry,
    super.key,
  });

  final RobotAlarmSummary? summary;
  final String? error;
  final Future<void> Function() onRetry;

  @override
  Widget build(BuildContext context) {
    if (error case final message?) {
      return Container(
        key: const Key('robot-alarm-unavailable-banner'),
        width: double.infinity,
        color: const Color(0xFFE5E7EB),
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
        child: Row(
          children: [
            const Icon(Icons.help_outline, color: Color(0xFF374151)),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                message,
                style: const TextStyle(
                  color: Color(0xFF374151),
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
            TextButton(
              key: const Key('robot-alarm-retry'),
              onPressed: onRetry,
              child: const Text('Yeniden dene'),
            ),
          ],
        ),
      );
    }

    final active = summary;
    if (active == null || !active.hasActiveRobotAlarm) {
      return const SizedBox.shrink();
    }

    final severe =
        active.highestSeverity == 'fatal' || active.highestSeverity == 'error';
    final foreground = severe ? Colors.white : const Color(0xFF92400E);
    return Container(
      key: const Key('robot-alarm-banner'),
      width: double.infinity,
      color: severe ? const Color(0xFF7C2D12) : const Color(0xFFFEF3C7),
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
      child: Row(
        children: [
          Icon(Icons.warning_amber, color: foreground),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              severe
                  ? 'Robot işlemi için müdahale gerekiyor'
                  : 'Robot uyarısı mevcut',
              style: TextStyle(color: foreground, fontWeight: FontWeight.w700),
            ),
          ),
          TextButton(
            key: const Key('robot-alarm-refresh'),
            onPressed: onRetry,
            child: Text('Yenile', style: TextStyle(color: foreground)),
          ),
        ],
      ),
    );
  }
}

class NoticeBanner extends StatelessWidget {
  const NoticeBanner({required this.message, super.key});

  final String message;

  @override
  Widget build(BuildContext context) => Container(
    key: const Key('notice-banner'),
    width: double.infinity,
    color: const Color(0xFFDBEAFE),
    padding: const EdgeInsets.all(12),
    child: Text(message, style: const TextStyle(fontWeight: FontWeight.w700)),
  );
}

class OperatorUserBanner extends StatelessWidget {
  const OperatorUserBanner({required this.user, super.key});

  final AuthenticatedUser? user;

  @override
  Widget build(BuildContext context) {
    final current = user;
    if (current == null) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final displayName = current.fullName.trim().isNotEmpty
        ? current.fullName
        : current.username;
    final sicil =
        current.employeeNo != null && current.employeeNo!.trim().isNotEmpty
        ? current.employeeNo!
        : '—';

    return Container(
      key: const Key('operator-user-banner'),
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
      color: theme.colorScheme.surfaceContainerHighest.withValues(alpha: 0.7),
      child: Row(
        children: [
          Icon(
            Icons.person_outline,
            size: 16,
            color: theme.colorScheme.primary,
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              '$displayName (Sicil: $sicil)',
              style: theme.textTheme.labelMedium?.copyWith(
                fontWeight: FontWeight.w600,
                color: theme.colorScheme.onSurface,
              ),
              overflow: TextOverflow.ellipsis,
            ),
          ),
        ],
      ),
    );
  }
}
