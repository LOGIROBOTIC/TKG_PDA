class ApiProblem implements Exception {
  const ApiProblem({
    required this.status,
    required this.code,
    required this.detail,
    required this.correlationId,
  });

  final int status;
  final String code;
  final String detail;
  final String? correlationId;

  bool get isUnauthorized => status == 401;
  bool get shouldRefetch => status == 409 || status == 422 || status == 428;

  String get operatorMessage => switch (status) {
    400 => detail.isEmpty ? 'Gönderilen bilgiler geçersiz.' : detail,
    401 => 'Oturumunuz sona erdi. Lütfen yeniden giriş yapın.',
    403 => 'Bu işlem için yetkiniz bulunmuyor.',
    404 => 'İstenen kayıt bulunamadı.',
    409 => detail.isEmpty ? 'İşlem mevcut durumla çakışıyor.' : detail,
    422 => detail.isEmpty ? 'İşlem mevcut saha durumuna uygun değil.' : detail,
    428 => 'İşlem için güncel sürüm bilgisi gerekli.',
    429 => 'Çok fazla istek gönderildi. Kısa süre sonra tekrar deneyin.',
    503 => 'Sistem geçici olarak kullanılamıyor.',
    _ => 'İşlem tamamlanamadı. Lütfen tekrar deneyin.',
  };

  @override
  String toString() => 'ApiProblem($status, $code)';
}

class ConnectivityException implements Exception {
  const ConnectivityException([
    this.message = 'Backend bağlantısı kurulamadı.',
  ]);

  final String message;
}

class AmbiguousMutationException implements Exception {
  const AmbiguousMutationException({
    required this.idempotencyKey,
    this.message = 'İsteğin sunucuya ulaşıp ulaşmadığı doğrulanamadı.',
  });

  final String idempotencyKey;
  final String message;
}
