// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IStrings } from '../../../IStrings';

export const strings: IStrings = {
  emptyList: 'No hay grupos administrados por GMM',
  loading: 'Cargando',
  addOwner204Message: 'Agregado correctamente.',
  addOwner400Message: 'GMM ya está agregado como propietario.',
  addOwner403Message: 'No tiene permiso para completar esta operación.',
  addOwnerErrorMessage:
    'Estamos teniendo problemas para agregar GMM como propietario. Inténtalo de nuevo más tarde.',
  bannerMessageStart:
    '¿Necesitas ayuda? Haz click aquí para aprender más sobre como Membership Management funciona en tu organización.',
  bannerMessageLink: 'Haz click aquí',
  bannerMessageEnd: ' para aprender más sobre como Membership Management funciona en tu organización.',
  okButton: 'Aceptar',
  groupIdHeader: 'Introduzca el ID de grupo',
  groupIdPlaceHolder: 'ID de grupo',
  addOwnerButton: 'Agregar GMM como propietario',
  membershipManagement: 'Membership Management',
  learnMembershipManagement:
    'Aprenda cómo funciona Membership Management en su organización',
  AdminConfig: {
      labels: {
          pageTitle: "Configuración de Administrador",
          hyperlinks: "Ligas",
          description: "Incluye ligas con información específica sobre cómo funciona XMM en tu organización para que los usuarios puedan aprovecharlo al máximo."
      },
      hyperlinkContainer: {
          address: "URL",
          addHyperlink: "URL de la liga",
          dashboardTitle: "Dashboard",
          dashboardDescription: "Esta es la liga que se muestra en la esquina superior derecha del dashboard. Te lleva a un sitio interno que tiene todos los detalles sobre cómo aprovechar XMM en tu organización. Esto podría incluir preguntas frecuentes, información de contacto, SLAs, etc.",
          invalidUrl: "URL inválida"
      }
  },
  JobDetails: {
    labels: {
      pageTitle: 'Detalle de Membresía',
      sectionTitle: 'Detalle de Membresía',
      lastModifiedby: 'Última vez modificado por',
      groupLinks: 'Links del grupo',
      destination: 'Destino',
      type: 'Tipo',
      name: 'Nombre',
      ID: 'ID',
      configuration: 'Configuración',
      startDate: 'Fecha de inicio',
      endDate: 'Fecha de terminación',
      lastRun: 'Última sincronización',
      nextRun: 'Próxima sincronización',
      frequency: 'Frecuencia',
      frequencyDescription: 'Cada {0} hrs',
      requestor: 'Solicitante',
      increaseThreshold: 'Aumentar límite',
      decreaseThreshold: 'Disminuir límite',
      thresholdViolations: 'Violaciones del límite',
      sourceParts: 'Partes de origen',
      membershipStatus: 'Estado de la membresía',
      sync: 'Sync',
      enabled: 'Activo',
      disabled: 'Inactivo',
    },
    descriptions: {
      lastModifiedby: 'Usuario quien hizo el último cambio a este grupo.',
      startDate: 'Fecha en la que este grupo fue integrado a GMM.',
      endDate: 'Fecha de la última sincronización de este grupo.',
      type: 'Tipo de sincronización.',
      id: 'ID de objecto del grupo destino.',
      lastRun: 'Fecha de la última sincronización de este grupo.',
      nextRun: 'Fecha de la próxima sincronización de este grupo.',
      frequency: 'Frecuencia de la sincronización de este grupo.',
      requestor: 'Usuario quien solicitó la integración a GMM.',
      increaseThreshold:
        'Número de usuarios que pueden ser añadidos al grupo destino, expresados como porcentaje del tamaño actual del grupo.',
      decreaseThreshold:
        'Número de usuarios que pueden ser removidos del grupo destino, expresados como porcentaje del tamaño actual del grupo.',
      thresholdViolations:
        'Número de ocasiones en las que se han excedido los límites.',
    },
    MessageBar: {
      dismissButtonAriaLabel: 'Cerrar',
    },
    openInAzure: 'Abrir en Azure',
    viewDetails: 'Ver Detalles',
    editButton: 'Editar',
  },
  JobsList: {
    listOfMemberships: 'Lista de membresías',
    ShimmeredDetailsList: {
      toggleSelection: 'Alternar selección',
      toggleAllSelection: 'Alternar selecciones para todo',
      selectRow: 'seleccionar fila',
      ariaLabelForShimmer: 'Recuperando contenido',
      ariaLabelForGrid: 'Detalles de contenido',
      columnNames: {
        name: 'Nombre',
        type: 'Tipo',
        lastRun: 'Última sincronización',
        nextRun: 'Próxima sincronización',
        status: 'Status',
        actionRequired: 'Acción requerida',
      },
    },
    MessageBar: {
      dismissButtonAriaLabel: 'Cerrar',
    },
    PagingBar: {
      previousPage: 'Ant',
      nextPage: 'Sig',
      page: 'Página',
      of: 'de',
      display: 'Mostrando',
      items: 'Elementos por página',
    },
    JobsListFilter: {
      filters: {
        ID: {
          label: 'ID',
          placeholder: 'Buscar',
          validationErrorMessage: 'GUID inválido!',
        },
        status: {
          label: 'Estado',
          options: {
            all: 'Todos',
            enabled: 'Activo',
            disabled: 'Inactivo',
          },
        },
        actionRequired: {
          label: 'Acción requerida',
          options: {
            all: 'Todos',
            thresholdExceeded: 'Límite excedido',
            customerPaused: 'Pausado por el cliente',
            membershipDataNotFound: 'Datos de membresía no encontrados',
            destinationGroupNotFound: 'Grupo de destino no encontrado',
            notOwnerOfDestinationGroup:
              'No es propietario del grupo de destino',
            securityGroupNotFound: 'Grupo de seguridad no encontrado',
          },
        },
      },
      filterButtonText: 'Filtrar',
      clearButtonTooltip: 'Eliminar filtros',
    },
    NoResults: 'No se encontro ninguna membresía',
  },
  welcome: 'Bienvenido',
  back: 'Regresar',
  version: 'Versión',
};
