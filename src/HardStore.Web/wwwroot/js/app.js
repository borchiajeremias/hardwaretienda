/**
 * wwwroot/js/app.js
 * ---------------------------------------------------------------------------
 * Interacción de la tienda: agregar al carrito, drawer, cotización en vivo,
 * cupón, medios de pago y avisos.
 *
 * Toda cuenta de dinero la hace el servidor: cada cambio del carrito dispara
 * un POST a /api/cotizar y la pantalla muestra lo que responde. El navegador
 * no multiplica ni descuenta nada.
 */

import { Carrito } from './carrito.js';

/* ---------------------------- Utilidades -------------------------------- */

const $ = (sel, ctx = document) => ctx.querySelector(sel);
const $$ = (sel, ctx = document) => Array.from(ctx.querySelectorAll(sel));

const formateador = new Intl.NumberFormat('es-AR', {
  style: 'currency',
  currency: 'ARS',
  minimumFractionDigits: 2,
});

const precio = (n) => formateador.format(Number(n) || 0);

const tokenCsrf = () => $('input[name="__RequestVerificationToken"]')?.value ?? '';

/* ------------------------------ Toasts ---------------------------------- */

const contenedorToasts = $('[data-toasts]');

function toast(mensaje, tipo = 'info', ms = 3200) {
  if (!contenedorToasts) return;

  const el = document.createElement('div');
  el.className = `toast toast--${tipo}`;
  el.setAttribute('role', tipo === 'error' ? 'alert' : 'status');
  el.textContent = mensaje;              // textContent: nunca innerHTML con datos
  contenedorToasts.appendChild(el);

  setTimeout(() => {
    el.classList.add('toast--saliendo');
    el.addEventListener('animationend', () => el.remove(), { once: true });
  }, ms);
}

/* --------------------------- Capas y overlay ---------------------------- */

const overlay = $('[data-overlay]');
const drawer = $('[data-carrito]');

function abrirDrawer() {
  if (!drawer) return;
  drawer.hidden = false;
  if (overlay) overlay.hidden = false;
  document.body.style.overflow = 'hidden';
  cotizar();
}

function cerrarDrawer() {
  if (!drawer) return;
  drawer.hidden = true;
  if (overlay) overlay.hidden = true;
  document.body.style.overflow = '';
}

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && drawer && !drawer.hidden) cerrarDrawer();
});
overlay?.addEventListener('click', cerrarDrawer);

/* ------------------------ Cotización en el servidor --------------------- */

let ultimaCotizacion = null;
let peticion = null;

/** Estado elegido en el drawer: cupón y plan de pago. */
const estado = {
  cupon: sessionStorage.getItem('hardstore.cupon') || '',
  plan: sessionStorage.getItem('hardstore.plan') || '',
};

async function cotizar() {
  const items = Carrito.items();

  pintarContador(Carrito.unidades());

  if (items.length === 0) {
    ultimaCotizacion = null;
    pintarDrawerVacio();
    return null;
  }

  peticion?.abort();
  peticion = new AbortController();

  try {
    const respuesta = await fetch('/api/cotizar', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        RequestVerificationToken: tokenCsrf(),
      },
      body: JSON.stringify({ items, cupon: estado.cupon || null, plan: estado.plan || null }),
      signal: peticion.signal,
    });

    if (!respuesta.ok) throw new Error('El servidor no pudo cotizar el carrito.');

    const datos = await respuesta.json();
    ultimaCotizacion = datos;

    // El servidor puede haber recortado cantidades por falta de stock o
    // quitado productos: el carrito se alinea con lo que aceptó.
    Carrito.sincronizarCon(datos.lineas);

    pintarDrawer(datos);
    (datos.avisos ?? []).forEach((a) => toast(a, 'error', 4200));

    return datos;
  } catch (err) {
    if (err.name === 'AbortError') return null;
    toast(err.message || 'No se pudo actualizar el carrito.', 'error');
    return null;
  }
}

/* ----------------------------- Pintado ---------------------------------- */

function pintarContador(unidades) {
  const badge = $('[data-carrito-contador]');
  if (!badge) return;
  badge.textContent = String(unidades);
  badge.hidden = unidades === 0;
}

function pintarDrawerVacio() {
  const items = $('[data-carrito-items]');
  const vacio = $('[data-carrito-vacio]');
  const pie = $('[data-carrito-footer]');

  if (items) { items.replaceChildren(); items.hidden = true; }
  if (vacio) vacio.hidden = false;
  if (pie) pie.hidden = true;
}

function pintarDrawer(c) {
  const cont = $('[data-carrito-items]');
  const vacio = $('[data-carrito-vacio]');
  const pie = $('[data-carrito-footer]');
  if (!cont) return;

  cont.hidden = false;
  if (vacio) vacio.hidden = true;
  if (pie) pie.hidden = false;

  const frag = document.createDocumentFragment();

  c.lineas.forEach((l) => {
    const fila = document.createElement('div');
    fila.className = 'item';

    const img = document.createElement('img');
    img.className = 'item__img';
    img.src = '/' + (l.imagen || 'img/placeholder.svg');
    img.alt = '';
    img.loading = 'lazy';
    img.addEventListener('error', () => { img.src = '/img/placeholder.svg'; }, { once: true });

    const info = document.createElement('div');

    const nombre = document.createElement('p');
    nombre.className = 'item__nombre';
    nombre.textContent = l.nombre;

    const unit = document.createElement('p');
    unit.className = 'item__precio';
    unit.textContent = `${precio(l.precioUnitario)} c/u`;

    info.append(nombre, unit);

    if (l.promocion) {
      const promo = document.createElement('p');
      promo.className = 'resumen__promo';
      promo.textContent = l.bonificadas > 0
        ? `${l.promocion} · ${l.bonificadas} sin cargo`
        : `${l.promocion} · ${precio(l.descuentoPromo)} de descuento`;
      info.append(promo);
    }

    const contador = document.createElement('div');
    contador.className = 'contador';

    const menos = document.createElement('button');
    menos.type = 'button';
    menos.textContent = '−';
    menos.setAttribute('aria-label', `Quitar una unidad de ${l.nombre}`);
    menos.addEventListener('click', () => {
      Carrito.fijarCantidad(l.id, l.cantidad - 1);
      cotizar();
    });

    const valor = document.createElement('span');
    valor.className = 'contador__valor';
    valor.textContent = String(l.cantidad);

    const mas = document.createElement('button');
    mas.type = 'button';
    mas.textContent = '+';
    mas.disabled = l.cantidad >= l.stock;
    mas.setAttribute('aria-label', `Agregar una unidad de ${l.nombre}`);
    mas.addEventListener('click', () => {
      Carrito.fijarCantidad(l.id, l.cantidad + 1);
      cotizar();
    });

    contador.append(menos, valor, mas);
    info.append(contador);

    const acciones = document.createElement('div');
    acciones.className = 'item__acciones';

    const subtotal = document.createElement('span');
    subtotal.className = 'item__subtotal';
    subtotal.textContent = precio(l.subtotal);

    const quitar = document.createElement('button');
    quitar.type = 'button';
    quitar.className = 'item__quitar';
    quitar.textContent = 'Quitar';
    quitar.addEventListener('click', () => {
      Carrito.quitar(l.id);
      cotizar();
    });

    acciones.append(subtotal, quitar);
    fila.append(img, info, acciones);
    frag.appendChild(fila);
  });

  cont.replaceChildren(frag);

  // ------------------------- Totales del drawer -------------------------
  const set = (sel, texto) => { const el = $(sel); if (el) el.textContent = texto; };

  set('[data-total-bruto]', precio(c.bruto));
  set('[data-total-promos]', '- ' + precio(c.descuentoPromociones));
  set('[data-total-cupon]', '- ' + precio(c.descuentoCupon));
  set('[data-total-pago]', (c.ajustePago >= 0 ? '+ ' : '- ') + precio(Math.abs(c.ajustePago)));
  set('[data-total-final]', precio(c.total));
  set('[data-total-iva]', precio(c.iva));

  const filaPromos = $('[data-fila-promos]');
  if (filaPromos) filaPromos.hidden = c.descuentoPromociones <= 0;

  const filaCupon = $('[data-fila-cupon]');
  if (filaCupon) filaCupon.hidden = c.descuentoCupon <= 0;

  const filaPago = $('[data-fila-pago]');
  if (filaPago) filaPago.hidden = c.ajustePago === 0;

  const cuotas = $('[data-cuotas]');
  if (cuotas) {
    cuotas.hidden = c.plan.cuotas <= 1;
    if (c.plan.cuotas > 1) {
      cuotas.textContent = `${c.plan.cuotas} cuotas de ${precio(c.valorCuota)}`;
    }
  }

  const ahorro = $('[data-ahorro]');
  if (ahorro) {
    ahorro.hidden = c.ahorro <= 0;
    if (c.ahorro > 0) ahorro.textContent = `Ahorrás ${precio(c.ahorro)}`;
  }

  // Estado del cupón
  const cuponAplicado = $('[data-cupon-aplicado]');
  const cuponForm = $('[data-cupon-form]');
  if (cuponAplicado && cuponForm) {
    if (c.cupon) {
      cuponAplicado.hidden = false;
      cuponForm.hidden = true;
      const etiqueta = $('[data-cupon-codigo]');
      if (etiqueta) etiqueta.textContent = `${c.cupon} · ${c.cuponDescripcion ?? ''}`;
    } else {
      cuponAplicado.hidden = true;
      cuponForm.hidden = false;
    }
  }

  // Medios de pago con su total
  $$('[data-plan]').forEach((el) => {
    const clave = el.dataset.plan;
    const fila = (c.comparativa ?? []).find((x) => x.clave === clave);
    if (!fila) return;

    const monto = $('[data-plan-total]', el);
    if (monto) monto.textContent = precio(fila.total);

    const cuota = $('[data-plan-cuota]', el);
    if (cuota) {
      cuota.hidden = fila.cuotas <= 1;
      if (fila.cuotas > 1) cuota.textContent = `${fila.cuotas} × ${precio(fila.valorCuota)}`;
    }

    const radio = $('input[type=radio]', el);
    if (radio) {
      radio.checked = clave === c.plan.clave;
      el.classList.toggle('is-elegido', radio.checked);
    }
  });

  // El checkout recibe el carrito por un campo oculto.
  const oculto = $('[data-items-json]');
  if (oculto) {
    oculto.value = JSON.stringify(Carrito.items());
  }
}

/* --------------------------- Eventos globales --------------------------- */

// Agregar al carrito (delegación: sirve para las cards del servidor y las nuevas)
document.addEventListener('click', (e) => {
  const btn = e.target.closest('[data-agregar]');
  if (!btn) return;

  const id = Number(btn.dataset.agregar);
  const r = Carrito.agregar(id, 1);

  if (!r.ok) {
    toast(r.motivo ?? 'No se pudo agregar el producto.', 'error');
    return;
  }

  toast(`«${btn.dataset.nombre || 'Producto'}» agregado al carrito.`, 'exito');

  const original = btn.textContent;
  btn.textContent = '✓ Agregado';
  setTimeout(() => { btn.textContent = original; }, 1100);

  cotizar();
});

$$('[data-abrir-carrito]').forEach((b) => b.addEventListener('click', abrirDrawer));
$$('[data-cerrar-carrito]').forEach((b) => b.addEventListener('click', cerrarDrawer));

$('[data-vaciar-carrito]')?.addEventListener('click', () => {
  if (Carrito.estaVacio()) return;
  Carrito.vaciar();
  estado.cupon = '';
  sessionStorage.removeItem('hardstore.cupon');
  cotizar();
  toast('Carrito vacío.', 'info', 2000);
});

// Cupón
$('[data-aplicar-cupon]')?.addEventListener('click', () => {
  const input = $('[data-cupon-input]');
  const codigo = (input?.value ?? '').trim().toUpperCase();
  if (!codigo) return;

  estado.cupon = codigo;
  sessionStorage.setItem('hardstore.cupon', codigo);
  cotizar();
});

$('[data-cupon-input]')?.addEventListener('keydown', (e) => {
  if (e.key === 'Enter') {
    e.preventDefault();
    $('[data-aplicar-cupon]')?.click();
  }
});

$('[data-quitar-cupon]')?.addEventListener('click', () => {
  estado.cupon = '';
  sessionStorage.removeItem('hardstore.cupon');
  const input = $('[data-cupon-input]');
  if (input) input.value = '';
  cotizar();
});

// Medio de pago
$$('[data-plan] input[type=radio]').forEach((radio) => {
  radio.addEventListener('change', () => {
    estado.plan = radio.value;
    sessionStorage.setItem('hardstore.plan', radio.value);
    cotizar();
  });
});

// Ir al checkout llevando el carrito, el cupón y el plan por querystring
$('[data-ir-checkout]')?.addEventListener('click', () => {
  if (Carrito.estaVacio()) {
    toast('Tu carrito está vacío.', 'error');
    return;
  }

  const params = new URLSearchParams();
  params.set('items', JSON.stringify(Carrito.items()));
  if (estado.cupon) params.set('cupon', estado.cupon);
  if (estado.plan) params.set('plan', estado.plan);

  window.location.href = '/Checkout/Index?' + params.toString();
});

/* ------------------------------ Arranque -------------------------------- */

Carrito.alCambiar((items) => pintarContador(items.reduce((a, i) => a + i.cantidad, 0)));

// Recuperar el cupón guardado en el input
const inputCupon = $('[data-cupon-input]');
if (inputCupon && estado.cupon) inputCupon.value = estado.cupon;

// Si la página tiene el drawer, se cotiza al entrar para tener el contador y
// los totales al día (por ejemplo si cambió el stock desde la última visita).
if (!Carrito.estaVacio()) cotizar();

// El checkout mantiene su propio formulario sincronizado.
const itemsOcultos = $('[data-items-json]');
if (itemsOcultos) itemsOcultos.value = JSON.stringify(Carrito.items());

// Al confirmar la compra hay que vaciar el carrito local.
if (document.body.dataset.compraConfirmada === 'true') {
  Carrito.vaciar();
  sessionStorage.removeItem('hardstore.cupon');
  sessionStorage.removeItem('hardstore.plan');
}
