/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './Areas/**/*.cshtml',
    './Views/**/*.cshtml',
    './wwwroot/js/**/*.js'
  ],
  theme: {
    extend: {
      colors: {
        ink: '#07090D',
        panel: '#0D1219',
        raised: '#121923',
        line: '#202C38',
        signal: '#B8F36B',
        cyan: '#55D6FF',
        ember: '#FF8A4C',
        accent: {
          blue: '#55D6FF',
          purple: '#A78BFA',
          teal: '#5EEAD4',
          red: '#FB7185',
          amber: '#FBBF24'
        }
      },
      fontFamily: {
        sans: ['Space Grotesk Variable', 'Inter Variable', 'sans-serif'],
        mono: ['IBM Plex Mono', 'JetBrains Mono', 'monospace']
      },
      boxShadow: {
        signal: '0 0 0 1px rgba(184,243,107,.12), 0 24px 70px rgba(0,0,0,.35)',
        panel: '0 24px 80px rgba(0,0,0,.24)'
      }
    }
  },
  plugins: [require('@tailwindcss/typography')]
};
