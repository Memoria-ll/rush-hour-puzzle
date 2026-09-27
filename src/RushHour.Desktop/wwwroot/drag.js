(() => {
    'use strict';
    let dotnet, size = 6, vehicles = [], locked = true, revision = 0, drag = null, pending = null, previousFocus = null;
    const root = () => document.getElementById('game-board');
    const buttons = () => document.querySelectorAll('.vehicle[data-id]');
    function position(element, v) {
        element.style.left = `${(100 * v.x / size).toFixed(4)}%`;
        element.style.top = `${(100 * v.y / size).toFixed(4)}%`;
    }
    function limits(v) {
        const occupied = (delta) => {
            const x = v.x + (v.horizontal ? delta : 0), y = v.y + (v.horizontal ? 0 : delta);
            if (x < 0 || y < 0 || x + v.width > size || y + v.height > size) return true;
            return vehicles.some(o => o.id !== v.id && x < o.x + o.width && x + v.width > o.x && y < o.y + o.height && y + v.height > o.y);
        };
        let min = 0, max = 0;
        while (!occupied(min - 1)) min--;
        while (!occupied(max + 1)) max++;
        if (v.isTarget && v.x + max + v.width === size) max++;
        return { min, max };
    }
    function reset() {
        if (drag) {
            const { element, pointerId } = drag;
            drag = null;
            element.style.transform = '';
            element.classList.remove('dragging');
            if (element.hasPointerCapture(pointerId)) element.releasePointerCapture(pointerId);
        }
    }
    function down(event) {
        const element = event.target.closest('.vehicle[data-id]');
        if (!element || !root()?.contains(element) || locked || pending || drag || event.button !== 0) return;
        const v = vehicles.find(x => x.id === Number(element.dataset.id));
        if (!v) return;
        event.preventDefault();
        element.focus({ preventScroll: true });
        document.querySelectorAll('.vehicle.hint').forEach(e => e.classList.remove('hint'));
        drag = { element, v, ...limits(v), start: v.horizontal ? event.clientX : event.clientY, cell: root().getBoundingClientRect().width / size, delta: 0, pointerId: event.pointerId, revision };
        element.setPointerCapture(event.pointerId);
        element.classList.add('dragging');
    }
    function move(event) {
        if (!drag || event.pointerId !== drag.pointerId) return;
        const d = drag;
        const offset = (d.v.horizontal ? event.clientX : event.clientY) - d.start;
        d.delta = Math.max(d.min, Math.min(d.max, offset / d.cell));
        const px = d.delta * d.cell;
        d.element.style.transform = `translate3d(${d.v.horizontal ? px : 0}px,${d.v.horizontal ? 0 : px}px,0)`;
    }
    async function commit(v, delta, expected) {
        if (!delta || pending || locked) return;
        const element = [...buttons()].find(e => Number(e.dataset.id) === v.id);
        if (!element) return;
        const transaction = { element, vehicle: v, revision: expected };
        pending = transaction;
        // Snap to the drop cell in this event, before crossing the async .NET
        // boundary. Clearing the drag transform must never expose the old cell.
        position(element, { ...v, x: v.x + (v.horizontal ? delta : 0), y: v.y + (v.horizontal ? 0 : delta) });
        let accepted = false;
        try { accepted = await dotnet.invokeMethodAsync('CommitMove', v.id, delta, expected); }
        catch (error) { console.error('Move failed', error); }
        finally {
            if (!accepted && pending === transaction) {
                if (revision === expected) position(element, v);
                pending = null;
            }
            // A successful call stays locked until sync supplies the confirmed
            // board. The response can arrive either before or after that render.
        }
    }
    function up(event) {
        if (!drag || event.pointerId !== drag.pointerId) return;
        move(event);
        const d = drag, delta = Math.round(d.delta);
        reset();
        void commit(d.v, delta, d.revision);
    }
    function key(event) {
        const modal = document.querySelector('.modal');
        if (modal) {
            if (event.key === 'Escape') { event.preventDefault(); void dotnet.invokeMethodAsync('CloseHelp'); }
            if (event.key === 'Tab') { event.preventDefault(); modal.querySelector('button')?.focus(); }
            return;
        }
        const element = event.target.closest('.vehicle[data-id]');
        if (!element || locked || pending) return;
        const v = vehicles.find(x => x.id === Number(element.dataset.id));
        if (!v) return;
        if (event.key === 'Escape') { reset(); return; }
        const delta = v.horizontal ? ({ ArrowLeft: -1, ArrowRight: 1 }[event.key]) : ({ ArrowUp: -1, ArrowDown: 1 }[event.key]);
        if (!delta) return;
        event.preventDefault();
        const range = limits(v);
        const step = event.shiftKey ? (delta < 0 ? range.min : range.max) : delta;
        if (step >= range.min && step <= range.max) void commit(v, step, revision);
    }
    function canceled(event) { if (drag && event.pointerId === drag.pointerId) reset(); }
    window.rushHour = {
        init(reference) {
            dotnet = reference;
            document.addEventListener('pointerdown', down);
            document.addEventListener('pointermove', move);
            document.addEventListener('pointerup', up);
            document.addEventListener('pointercancel', canceled);
            document.addEventListener('lostpointercapture', canceled);
            document.addEventListener('keydown', key);
            window.addEventListener('blur', reset);
            window.addEventListener('resize', reset);
        },
        sync(newSize, newVehicles, disabled, newRevision) {
            reset(); size = newSize; vehicles = newVehicles; locked = disabled; revision = newRevision;
            if (pending && newRevision > pending.revision) pending = null;
            buttons().forEach(e => {
                const v = vehicles.find(v => v.id === Number(e.dataset.id));
                if (v && (!pending || pending.element !== e)) position(e, v);
                e.style.transform = '';
            });
        },
        hint(id) { buttons().forEach(e => e.classList.toggle('hint', Number(e.dataset.id) === id)); },
        help(open) {
            reset();
            const app = document.querySelector('.app');
            if (app) app.inert = open;
            if (open) { previousFocus = document.activeElement; document.querySelector('.modal button')?.focus(); }
            else { if (previousFocus?.isConnected) previousFocus.focus(); previousFocus = null; }
        },
        dispose() {
            reset();
            pending = null;
            document.removeEventListener('pointerdown', down);
            document.removeEventListener('pointermove', move);
            document.removeEventListener('pointerup', up);
            document.removeEventListener('pointercancel', canceled);
            document.removeEventListener('lostpointercapture', canceled);
            document.removeEventListener('keydown', key);
            window.removeEventListener('blur', reset);
            window.removeEventListener('resize', reset);
            dotnet = null;
        }
    };
})();
