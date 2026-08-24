import 'package:flutter/material.dart';

import '../../core/errors/api_problem.dart';

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
        if (problem.correlationId != null)
          Text(
            'Destek referansı: ${problem.correlationId}',
            key: const Key('problem-correlation'),
            style: const TextStyle(fontSize: 12),
          ),
      ],
    ),
  );
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
