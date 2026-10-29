import { apiRequest } from '@/Services/apiClient'

import type {
  CustomerPhotoInfo,
  CustomerPhotoInsertResult,
  CustomerPhotoListParams,
  CustomerPhotoPage,
  CustomerProfilePhotoInfo,
  ProfilePhotoUpdateInfo,
} from '@/types/customerPhoto'
import { PHOTO_PAGE_SIZE } from '@/types/customerPhoto'

/**
 * Customer Photo Service — álbum da cliente (feature 009, skill
 * `react-architecture`: classe singleton, métodos finos sobre o `apiClient`).
 *
 * Nenhum método sobe bytes: os arquivos entram por `imageService.upload`
 * (`POST /images`, feature 004) e aqui só circula `imageId` (research R7). Não
 * existe rota avulsa de foto — a renovação de um link vencido é
 * `GET /images/{imageId}` (research R10).
 *
 * Os erros chegam como classes do transporte (`NotFoundError`, `ApiError`,
 * `UnauthorizedError`), como em `customerService`: em 200 o serviço não checa
 * `sucesso`, que é a exceção formal registrada no plano.
 *
 * A query de listagem é montada AQUI, e não no `options.query` do `apiClient`,
 * porque `apiRequest` fixa `pageSize=20` em toda query (contrato da HU 001) e a
 * grade precisa de página de 24 (contracts/ui.md §2, research R3/R8).
 */

/** `?cursor=&pageSize=24&favorites=` — vazios omitidos, ordem dos parâmetros fixa. */
const buildListPath = (params: CustomerPhotoListParams): string => {
  const search = new URLSearchParams()

  const cursor = params.cursor
  if (cursor !== undefined && cursor !== null && cursor > 0) {
    search.set('cursor', String(cursor))
  }

  search.set('pageSize', String(params.pageSize ?? PHOTO_PAGE_SIZE))
  if (params.favorites === true) search.set('favorites', 'true')

  const query = search.toString()

  return `/customers/${String(params.customerId)}/photos${query === '' ? '' : `?${query}`}`
}

class CustomerPhotoService {
  /** `GET /customers/{id}/photos?cursor&pageSize=24&favorites` (FR-010, R8). */
  async list(params: CustomerPhotoListParams): Promise<CustomerPhotoPage> {
    return apiRequest<CustomerPhotoPage>('GET', buildListPath(params))
  }

  /**
   * `POST /customers/{id}/photos` — anexa uma imagem já guardada ao álbum.
   * A tela manda `source: 'web'`; sem descrição (US2-1). `created: false` na
   * resposta significa "essa foto já estava no álbum" (FR-006).
   */
  async attach(
    customerId: number,
    imageId: number,
    description?: string | null,
  ): Promise<CustomerPhotoInsertResult> {
    return apiRequest<CustomerPhotoInsertResult>('POST', `/customers/${String(customerId)}/photos`, {
      body: {
        imageId,
        ...(description === undefined || description === '' ? {} : { description }),
        source: 'web',
      },
    })
  }

  /**
   * `PUT .../photos/{photoId}/description` (FR-007). Texto vazio é enviado como
   * `null`: "apagar a descrição" tem de chegar como nulo, não como "".
   */
  async setDescription(
    customerId: number,
    photoId: number,
    description: string | null,
  ): Promise<CustomerPhotoInfo> {
    return apiRequest<CustomerPhotoInfo>(
      'PUT',
      `/customers/${String(customerId)}/photos/${String(photoId)}/description`,
      { body: { description: description?.trim() === '' ? null : description } },
    )
  }

  /** `PUT .../photos/{photoId}/favorite` (FR-008). */
  async favorite(customerId: number, photoId: number): Promise<CustomerPhotoInfo> {
    return apiRequest<CustomerPhotoInfo>(
      'PUT',
      `/customers/${String(customerId)}/photos/${String(photoId)}/favorite`,
    )
  }

  /** `DELETE .../photos/{photoId}/favorite` — operação própria por verbo (R11). */
  async unfavorite(customerId: number, photoId: number): Promise<CustomerPhotoInfo> {
    return apiRequest<CustomerPhotoInfo>(
      'DELETE',
      `/customers/${String(customerId)}/photos/${String(photoId)}/favorite`,
    )
  }

  /** `DELETE .../photos/{photoId}` → 204. Excluir a origem limpa o perfil (FR-015). */
  async remove(customerId: number, photoId: number): Promise<void> {
    await apiRequest<undefined>('DELETE', `/customers/${String(customerId)}/photos/${String(photoId)}`)
  }

  /**
   * `PUT /customers/{id}/profile-photo` (FR-011/FR-012). `imageId` é o recorte
   * quadrado gerado no navegador; ausente, o círculo usa o arquivo da foto.
   */
  async setProfile(
    customerId: number,
    photoId: number,
    imageId?: number,
  ): Promise<CustomerProfilePhotoInfo> {
    const body: ProfilePhotoUpdateInfo = {
      photoId,
      ...(imageId === undefined ? {} : { imageId }),
    }

    return apiRequest<CustomerProfilePhotoInfo>(
      'PUT',
      `/customers/${String(customerId)}/profile-photo`,
      { body },
    )
  }

  /** `DELETE /customers/{id}/profile-photo` → 204. A foto continua no álbum (FR-015). */
  async removeProfile(customerId: number): Promise<void> {
    await apiRequest<undefined>('DELETE', `/customers/${String(customerId)}/profile-photo`)
  }
}

export const customerPhotoService = new CustomerPhotoService()
export default CustomerPhotoService
