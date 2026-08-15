//--------------------------//
//--------定义应用市场目录的前端只读契约---------//
//--------Defines the frontend read-only app-catalog contract--------//
//-------------------------//
export type AppStoreCategoryId = 'explore' | StoreAppCategory;

export type StoreAppCategory = 'create' | 'work' | 'tools' | 'development';

export interface StoreApp {
  readonly publisherId: string;
  readonly id: string;
  readonly name: string;
  readonly category: StoreAppCategory;
  readonly eyebrow: string;
  readonly description: string;
  readonly overview: string;
  readonly features: readonly string[];
  readonly imageUrl: string;
}

export interface AppCatalogResponse {
  readonly format: 'amseok-app-catalog-v1';
  readonly revision: string;
  readonly generatedAt: string;
  readonly refreshedAt: string;
  readonly isStale: boolean;
  readonly apps: readonly StoreApp[];
}
