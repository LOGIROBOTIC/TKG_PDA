import { ArrowLeft, MapPinOff } from 'lucide-react'
import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return <div className="not-found"><MapPinOff /><span className="eyebrow">404</span><h1>Bu yönetim sayfası bulunamadı.</h1><p>Adres değişmiş olabilir veya bu alan henüz uygulama kapsamında değildir.</p><Link className="button button--primary button--md" to="/"><ArrowLeft size={17} /> Genel bakışa dön</Link></div>
}
