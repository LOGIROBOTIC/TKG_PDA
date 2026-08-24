import { CheckCircle2, CircleAlert, X } from 'lucide-react'
import { createContext, useCallback, useContext, useMemo, useState, type PropsWithChildren } from 'react'
import { ApiProblem } from '../api/client'

type ToastTone = 'success' | 'error'
interface ToastItem { id: number; tone: ToastTone; title: string; message?: string }

interface ToastContextValue {
  success: (title: string, message?: string) => void
  error: (error: unknown, fallback?: string) => void
}

const ToastContext = createContext<ToastContextValue | null>(null)

export function ToastProvider({ children }: PropsWithChildren) {
  const [items, setItems] = useState<ToastItem[]>([])

  const push = useCallback((tone: ToastTone, title: string, message?: string) => {
    const id = Date.now() + Math.random()
    setItems((current) => [...current, { id, tone, title, message }])
    window.setTimeout(() => setItems((current) => current.filter((item) => item.id !== id)), 5500)
  }, [])

  const value = useMemo<ToastContextValue>(() => ({
    success: (title, message) => push('success', title, message),
    error: (error, fallback = 'İşlem tamamlanamadı') => {
      if (error instanceof ApiProblem) {
        const title = error.isConcurrencyConflict ? 'Kayıt başka bir yönetici tarafından değiştirildi' : fallback
        push('error', title, `${error.message} · ${error.code}${error.correlationId ? ` · ${error.correlationId}` : ''}`)
      } else {
        push('error', fallback, error instanceof Error ? error.message : 'Beklenmeyen bir hata oluştu.')
      }
    },
  }), [push])

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toast-region" aria-live="polite" aria-label="Bildirimler">
        {items.map((item) => (
          <div className={`toast toast--${item.tone}`} key={item.id}>
            {item.tone === 'success' ? <CheckCircle2 size={20} /> : <CircleAlert size={20} />}
            <div><strong>{item.title}</strong>{item.message && <span>{item.message}</span>}</div>
            <button aria-label="Bildirimi kapat" onClick={() => setItems((current) => current.filter((candidate) => candidate.id !== item.id))}><X size={16} /></button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}

export const useToast = () => {
  const value = useContext(ToastContext)
  if (!value) throw new Error('useToast must be used inside ToastProvider')
  return value
}
