const TIME_ZONE = 'Asia/Kolkata';
const SEVEN_DAYS_MS = 7 * 24 * 60 * 60 * 1000;

const dateFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: TIME_ZONE,
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
});

const dateTimeFormat = new Intl.DateTimeFormat('en-GB', {
  timeZone: TIME_ZONE,
  day: '2-digit',
  month: '2-digit',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

const relativeFormat = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });

function toDate(value: string | Date): Date {
  return value instanceof Date ? value : new Date(value);
}

function parts(format: Intl.DateTimeFormat, date: Date): Record<string, string> {
  const result: Record<string, string> = {};
  for (const part of format.formatToParts(date)) {
    result[part.type] = part.value;
  }
  return result;
}

/** An instant shown as dd/MM/yyyy in Indian time. */
export function formatDate(value: string | Date): string {
  const p = parts(dateFormat, toDate(value));
  return `${p['day']}/${p['month']}/${p['year']}`;
}

/** An instant shown as dd/MM/yyyy HH:mm (24-hour) in Indian time. */
export function formatDateTime(value: string | Date): string {
  const p = parts(dateTimeFormat, toDate(value));
  return `${p['day']}/${p['month']}/${p['year']} ${p['hour']}:${p['minute']}`;
}

/** Relative wording up to seven days old, the plain date after that. `exact` is always the full date and time. */
export function relativeTime(value: string | Date, now: Date = new Date()): { text: string; exact: string } {
  const date = toDate(value);
  const exact = formatDateTime(date);
  const diff = date.getTime() - now.getTime();
  if (Math.abs(diff) > SEVEN_DAYS_MS) {
    return { text: formatDate(date), exact };
  }
  const minutes = Math.round(diff / 60000);
  if (Math.abs(minutes) < 1) {
    return { text: 'just now', exact };
  }
  if (Math.abs(minutes) < 60) {
    return { text: relativeFormat.format(minutes, 'minute'), exact };
  }
  const hours = Math.round(minutes / 60);
  if (Math.abs(hours) < 24) {
    return { text: relativeFormat.format(hours, 'hour'), exact };
  }
  return { text: relativeFormat.format(Math.round(hours / 24), 'day'), exact };
}

/** A calendar day (yyyy-MM-dd) built from the local date parts, so no time-zone shift can change the day. */
export function toDateOnlyString(date: Date): string {
  const year = String(date.getFullYear()).padStart(4, '0');
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** Reads a yyyy-MM-dd value as a local date at midnight. */
export function parseDateOnly(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}
