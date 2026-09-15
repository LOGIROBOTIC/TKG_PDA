class ProductionSession {
  const ProductionSession({
    required this.id,
    required this.machineId,
    required this.variantId,
    required this.productGroupId,
    required this.mainPartId,
    required this.startedAt,
    this.endedAt,
  });

  final String id, machineId, variantId, productGroupId, mainPartId;
  final DateTime startedAt;
  final DateTime? endedAt;
  bool get isActive => endedAt == null;

  factory ProductionSession.fromJson(Map<String, dynamic> json) {
    String id(String field) {
      final value = json[field];
      if (value is! String || value.trim().isEmpty) {
        throw const FormatException('Invalid production identity');
      }
      return value;
    }

    final start = DateTime.tryParse(json['startedAt']?.toString() ?? '');
    final end = json['endedAt'] == null
        ? null
        : DateTime.tryParse(json['endedAt'].toString());
    if (start == null ||
        (json['endedAt'] != null && end == null) ||
        (json['status'] != (end == null ? 'ACTIVE' : 'ENDED')) ||
        (end != null && end.isBefore(start))) {
      throw const FormatException('Invalid production state or timestamp');
    }
    return ProductionSession(
      id: id('id'),
      machineId: id('machineId'),
      variantId: id('variantId'),
      productGroupId: id('productGroupId'),
      mainPartId: id('mainPartId'),
      startedAt: start,
      endedAt: end,
    );
  }
}

class ProductionStartCommand {
  const ProductionStartCommand({
    required this.machineId,
    required this.variantId,
    required this.productGroupId,
    required this.mainPartId,
  });
  final String machineId, variantId, productGroupId, mainPartId;
  Map<String, Object> toJson() => {
    'variantId': variantId,
    'productGroupId': productGroupId,
    'mainPartId': mainPartId,
  };
}
