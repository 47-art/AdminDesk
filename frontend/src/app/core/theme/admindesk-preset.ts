import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

/** Calm-blue theme: muted mid-blue primary, pale sky surfaces, dark slate text. */
export const AdminDeskPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#EEF4FB',
      100: '#DCE9F8',
      200: '#BCD3F0',
      300: '#8FB5E4',
      400: '#5E92D2',
      500: '#2F6DB8',
      600: '#285C9C',
      700: '#214E85',
      800: '#1A3F6C',
      900: '#143155',
      950: '#0D2038',
    },
    focusRing: { width: '2px', style: 'solid', color: '#2F6DB8', offset: '2px' },
    formField: { borderRadius: '6px' },
    colorScheme: {
      light: {
        primary: {
          color: '#2F6DB8',
          contrastColor: '#FFFFFF',
          hoverColor: '#285C9C',
          activeColor: '#214E85',
        },
        surface: {
          0: '#FFFFFF',
          50: '#F4F8FC',
          100: '#E8F0FA',
          200: '#D5E1EE',
          300: '#9FB3C8',
          400: '#7C8FA3',
          500: '#4B5B6E',
          600: '#3A4757',
          700: '#1F2A37',
          800: '#18212B',
          900: '#111820',
          950: '#0B1015',
        },
        formField: {
          borderColor: '#9FB3C8',
          hoverBorderColor: '#7C8FA3',
          focusBorderColor: '#2F6DB8',
          invalidBorderColor: '#B42318',
        },
        text: { color: '#1F2A37', mutedColor: '#4B5B6E' },
      },
    },
  },
  components: {
    card: { root: { borderRadius: '8px', shadow: '0 1px 2px rgba(31,42,55,0.08)' } },
    dialog: { root: { borderRadius: '8px' } },
  },
});
