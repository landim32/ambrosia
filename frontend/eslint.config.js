import js from '@eslint/js'
import reactHooks from 'eslint-plugin-react-hooks'
import tseslint from 'typescript-eslint'

/**
 * ESLint flat config (T010). Regras derivadas da constituição §IV e do plano:
 * nenhum `any`, sempre `interface`, arrow functions, `const` por padrão.
 */
export default tseslint.config(
  {
    ignores: [
      'dist/',
      'node_modules/',
      // Cache de pré-bundling do Vite (`npm run dev`): código de terceiros, não versionado.
      '.vite/',
      'test-results/',
      'coverage/',
      // Componentes gerados pelo shadcn/ui seguem o padrão da biblioteca.
      'src/components/ui/**',
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.strictTypeChecked,
  ...tseslint.configs.stylisticTypeChecked,
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    plugins: {
      'react-hooks': reactHooks,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/consistent-type-definitions': ['error', 'interface'],
      'prefer-arrow-callback': 'error',
      'func-style': ['error', 'expression', { allowArrowFunctions: true }],
      'no-console': ['error', { allow: ['warn', 'error'] }],
      eqeqeq: ['error', 'always', { null: 'ignore' }],
      'prefer-const': 'error',
      '@typescript-eslint/consistent-type-imports': [
        'error',
        { prefer: 'type-imports', fixStyle: 'separate-type-imports' },
      ],
    },
  },
  {
    // Arquivos de teste: globais/assertions do Vitest e mocks menos estritos.
    files: ['src/**/*.test.{ts,tsx}', 'src/test/**/*'],
    rules: {
      '@typescript-eslint/no-unsafe-assignment': 'off',
      '@typescript-eslint/no-unsafe-member-access': 'off',
      '@typescript-eslint/no-unsafe-argument': 'off',
      '@typescript-eslint/no-unsafe-call': 'off',
      '@typescript-eslint/no-unsafe-return': 'off',
      '@typescript-eslint/consistent-type-imports': 'off',
      // `vi.mocked(servico.metodo)` é o idioma padrão de mock do Vitest.
      '@typescript-eslint/unbound-method': 'off',
      'func-style': 'off',
      'no-console': 'off',
    },
  },
  {
    // JavaScript puro (este config e scripts de Node) não tem type information.
    files: ['**/*.js', '**/*.mjs'],
    languageOptions: {
      globals: {
        process: 'readonly',
        console: 'readonly',
        URL: 'readonly',
      },
    },
    rules: {
      ...tseslint.configs.disableTypeChecked.rules,
      'no-console': 'off',
      'func-style': 'off',
    },
  },
)
