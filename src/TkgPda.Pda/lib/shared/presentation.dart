String actionLabel(String action) => switch (action) {
  'BRING_EMPTY' => 'Boş Kasa Getir',
  'REMOVE_FULL' => 'Dolu Kasayı Kaldır',
  'REMOVE_EMPTY' => 'Boş Kasayı Kaldır',
  _ => action,
};

String executionStateLabel(String state) => switch (state) {
  'REQUESTED' => 'Talep oluşturuldu',
  'QUEUED' => 'Sırada',
  'WAITING_FOR_ROBOT' => 'Robot bekleniyor',
  'ROBOT_ASSIGNED' => 'Robot atandı',
  'RUNNING' => 'Taşıma devam ediyor',
  'WAITING' => 'Geçiş için bekleniyor',
  'COMPLETED' => 'Tamamlandı',
  'FAILED' => 'Tamamlanamadı',
  'TERMINATED' => 'Sonlandırıldı',
  _ => 'Durum güncelleniyor',
};

String occupancyLabel(String state) => switch (state) {
  'VACANT' => 'Boş konum',
  'OCCUPIED' => 'Dolu konum',
  _ => 'Doluluk bilinmiyor',
};

String loadLabel(String state) => switch (state) {
  'EMPTY' => 'Boş kasa/palet',
  'FULL' => 'Dolu kasa/palet',
  _ => 'Yük durumu bilinmiyor',
};

String failureLabel(String? code, String? operatorMessage) {
  if (operatorMessage != null && operatorMessage.trim().isNotEmpty) {
    return operatorMessage;
  }
  return switch (code) {
    'WORKSITE_BUSY' => 'Konum şu anda kullanımda.',
    'PATH_UNAVAILABLE' => 'Robot hedef noktaya ulaşamıyor.',
    'EXECUTION_FAILED' => 'Robot görevi tamamlayamadı.',
    'TRAFFIC_WAITING' => 'Robot trafiği nedeniyle görev bekliyor.',
    _ => 'İşlem durumu kontrol ediliyor.',
  };
}

String readinessLabel(String? code) => switch (code) {
  'RDS_WORKSITE_NOT_MAPPED' => 'Robot noktası henüz eşlenmemiş.',
  'RDS_WORKSITE_NOT_PHYSICAL' =>
    'Eşlenen robot noktası fiziksel taşıma noktası değil.',
  'RDS_WORKSITE_STALE' =>
    'Robot noktası için güncel senkronizasyon kanıtı yok.',
  'RDS_WORKSITE_DISABLED' => 'Robot noktası kullanım dışı.',
  'RDS_WORKSITE_SYNC_FAILED' => 'Robot noktası senkronize değil.',
  'RDS_WORKSITE_LOCKED' => 'Robot noktası şu anda kullanımda.',
  _ => 'Robot yürütme hazırlığı tamamlanmamış.',
};

String knownOr(String? value) =>
    value == null || value.trim().isEmpty ? 'Tanımlı değil' : value;

/// The backend always supplies a friendly Turkish `disabledMessage` today.
/// This is defensive hardening only, so a raw uppercase code (e.g.
/// `SLOT_BUSY`) is never shown to the operator if that ever stops being true.
String disabledReason(String? disabledMessage) =>
    disabledMessage ?? 'Bu işlem şu anda kullanılamıyor.';
