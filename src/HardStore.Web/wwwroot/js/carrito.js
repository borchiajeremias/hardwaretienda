/**
 * wwwroot/js/carrito.js
 * ---------------------------------------------------------------------------
 * Carrito del lado del cliente, persistido en localStorage.
 *
 * Guarda SÓLO {id, cantidad}. Ningún precio: los precios, las promociones, el
 * cupón y el recargo del medio de pago los calcula el servidor en
 * POST /api/cotizar. Editar localStorage a mano no cambia lo que se cobra, y
 * ésa es exactamente la intención.
 *
 * Patrón observador: quien quiera enterarse de los cambios se suscribe con
 * alCambiar(fn). Así la UI no tiene que llamarse a sí misma.
 */

const CLAVE = 'hardstore.carrito.v2';
const MAX_UNIDADES = 99;

let items = cargar();
const suscriptores = new Set();

/* --------------------------- Persistencia ------------------------------- */

function cargar() {
  try {
    const crudo = localStorage.getItem(CLAVE);
    if (!crudo) return [];

    const datos = JSON.parse(crudo);
    if (!Array.isArray(datos)) return [];

    // localStorage lo puede editar el usuario: se sanea todo lo que entra.
    return datos
      .map((i) => ({
        id: Number(i.id),
        cantidad: Math.min(Math.max(parseInt(i.cantidad, 10) || 1, 1), MAX_UNIDADES),
      }))
      .filter((i) => Number.isInteger(i.id) && i.id > 0);
  } catch {
    // JSON corrupto o almacenamiento bloqueado (modo privado): arrancamos vacíos.
    return [];
  }
}

function guardar() {
  try {
    localStorage.setItem(CLAVE, JSON.stringify(items));
  } catch {
    /* Cuota llena o storage deshabilitado: el carrito sigue en memoria. */
  }
  notificar();
}

function notificar() {
  const copia = items.map((i) => ({ ...i }));
  suscriptores.forEach((fn) => fn(copia));
}

/* -------------------------------- API ----------------------------------- */

export const Carrito = {
  items: () => items.map((i) => ({ ...i })),

  unidades: () => items.reduce((acc, i) => acc + i.cantidad, 0),

  cantidad(id) {
    return items.find((i) => i.id === Number(id))?.cantidad ?? 0;
  },

  estaVacio: () => items.length === 0,

  /**
   * Agrega unidades. El tope de stock lo valida el servidor al cotizar; acá
   * sólo se respeta el máximo por línea.
   */
  agregar(id, cantidad = 1) {
    id = Number(id);
    if (!Number.isInteger(id) || id <= 0) return { ok: false };

    const suma = Math.max(1, parseInt(cantidad, 10) || 1);
    const existente = items.find((i) => i.id === id);

    if (existente) {
      if (existente.cantidad >= MAX_UNIDADES) {
        return { ok: false, motivo: `El máximo por producto es ${MAX_UNIDADES}.` };
      }
      existente.cantidad = Math.min(existente.cantidad + suma, MAX_UNIDADES);
    } else {
      items.push({ id, cantidad: Math.min(suma, MAX_UNIDADES) });
    }

    guardar();
    return { ok: true, cantidad: this.cantidad(id) };
  },

  fijarCantidad(id, cantidad) {
    id = Number(id);
    const n = parseInt(cantidad, 10) || 0;

    if (n <= 0) return this.quitar(id);

    const item = items.find((i) => i.id === id);
    if (!item) return { ok: false };

    item.cantidad = Math.min(n, MAX_UNIDADES);
    guardar();
    return { ok: true, cantidad: item.cantidad };
  },

  quitar(id) {
    const antes = items.length;
    items = items.filter((i) => i.id !== Number(id));
    if (items.length !== antes) guardar();
    return { ok: items.length !== antes };
  },

  vaciar() {
    items = [];
    guardar();
  },

  /** Ajusta el carrito a lo que el servidor aceptó (por recorte de stock). */
  sincronizarCon(lineas) {
    const validos = new Map((lineas ?? []).map((l) => [Number(l.id), Number(l.cantidad)]));
    let cambio = false;

    items = items.filter((i) => {
      if (!validos.has(i.id)) {
        cambio = true;
        return false;
      }
      const cantidadServidor = validos.get(i.id);
      if (cantidadServidor !== i.cantidad) {
        i.cantidad = cantidadServidor;
        cambio = true;
      }
      return true;
    });

    if (cambio) guardar();
    return cambio;
  },

  /** Devuelve la función para desuscribirse. Se ejecuta una vez de inmediato. */
  alCambiar(fn) {
    suscriptores.add(fn);
    fn(this.items());
    return () => suscriptores.delete(fn);
  },
};

/* ------------------- Sincronización entre pestañas ---------------------- */
window.addEventListener('storage', (e) => {
  if (e.key === CLAVE) {
    items = cargar();
    notificar();
  }
});
