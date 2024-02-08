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
  clickHere: 'Haz click aquí',
  bannerMessageEnd: ' para aprender más sobre como Membership Management funciona en tu organización.',
  okButton: 'Aceptar',
  groupIdHeader: 'Introduzca el ID de grupo',
  groupIdPlaceHolder: 'ID de grupo',
  addOwnerButton: 'Agregar GMM como propietario',
  membershipManagement: 'Membership Management',
  learnMembershipManagement:
    'Aprenda cómo funciona Membership Management en su organización',
  Components: {
    HyperlinkSetting: {
      address: "URL",
      addHyperlink: "Agregar URL",
      invalidUrl: "URL inválida"
    },
  },
  AdminConfig: {
      labels: {
          pageTitle: "Configuración de Administrador",
          hyperlinks: "Ligas",
          description: "Incluye ligas con información específica sobre cómo funciona XMM en tu organización para que los usuarios puedan aprovecharlo al máximo.",
          saveButton: "Guardar",
          saveSuccess:  "Guardado exitosamente."
      },
      dashboardLink: {
          title: "Dashboard",
          description: "Esta es la liga que se muestra en la esquina superior derecha del dashboard. Te lleva a un sitio interno que tiene todos los detalles sobre cómo aprovechar XMM en tu organización. Esto podría incluir preguntas frecuentes, información de contacto, SLAs, etc.",
      },
      outlookWarningLink: {
        title: "Instrucciones de destino",
        description: "Esta es la liga que aparece cuando Outlook está involucrado en el grupo seleccionado. Te lleva a un sitio interno para hacer la configuración adecuada al enviar correos electrónicos al grupo de Outlook.",
      },
      privacyPolicyLink: {
          title: "Política de Privacidad",
          description: "Esta es la liga que se muestra en la esquina inferior izquierda del dashboard. Te lleva a un sitio interno que tiene todos los detalles sobre cómo XMM maneja y almacena los datos de los usuarios.",
      },
  },
  Authentication: {
    loginFailed: 'Ocurrió un error inesperado durante el inicio de sesión.'
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
    Errors:{
      jobInProgress: 'La sincronización está en progreso. Por favor intente más tarde.',
      notGroupOwner: 'No eres propietario de este grupo.',
      internalError: 'No podemos procesar su solicitud en este momento. Por favor, inténtelo de nuevo más tarde.'
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
        destinationType: {
          label: 'Tipo de destinación',
          options: {
            all: 'Todos',
            channel: 'Canal',
            group: 'Grupo'
          }
        },
        destinationName: {
          label: 'Nombre de destinación',
          placeholder: 'Buscar',
        },
        ownerPeoplePicker: {
          label: 'Dueño',
          suggestionsHeaderText: 'Personas sugeridas',
          noResultsFoundText: 'No se encontraron resultados',
          loadingText: 'Cargando',
          selectionAriaLabel: 'Contactos seleccionados',
          removeButtonAriaLabel: 'Eliminar'
        },
      },
      filterButtonText: 'Filtrar',
      clearButtonTooltip: 'Eliminar filtros',
    },
    NoResults: 'No se encontro ninguna membresía',
  },
  ManageMembership: {
    manageMembershipButton: 'Administrar membresía',
    labels: {
      abandonOnboarding: '¿Abandonar Onboarding?',
      abandonOnboardingDescription: '¿Estás seguro de que quieres abandonar el onboarding en progreso y regresar?',
      alreadyOnboardedWarning: 'Este grupo ya está siendo sincronizado por GMM.',
      confirmAbandon: 'Sí, regresar',
      pageTitle: 'Manejo de Membresía',
      step1title: 'Paso 1: Selecciona el destino',
      step1description: 'Por favor selecciona el tipo de destino y el destino cuya membresía quieres administrar.',
      selectDestinationType: 'Seleccionar tipo de destino',
      searchGroupSuggestedText: 'Grupos sugeridos',
      searchDestination: 'Buscar destino',
      selectDestinationTypePlaceholder: 'Buscar un grupo',
      noResultsFound: 'No se encontraron resultados',
      appsUsed: 'Este grupo utiliza las siguientes aplicaciones.',
      outlookWarning: 'Hay configuraciones importantes a considerar antes de enviar correo a este grupo de Outlook. Sigue las instrucciones de tu organización.',
      ownershipWarning: 'Atención: GMM no es dueño de este grupo Por favor agrégalo como propietrario antes de continuar.',
      step2title: 'Paso 2: Configuración de ejecución ',
      step2description: '',
      advancedQuery: 'Consulta avanzada',
      advancedView: 'Vista avanzada',
      query: 'Consulta',
      validQuery: 'Consulta válida.',
      invalidQuery: 'Error al analizar la consulta. Asegúrese de que sea JSON válido.',
      step3title: 'Paso 3: Configuración de Membresía',
      step3description: 'Defina la fuente de la membresía para el destino.',
      selectStartDate: 'Selecciona una opción para comenzar a administrar la membresía',
      ASAP: 'Lo antes posible',
      requestedDate: 'Solicitar fecha', 
      selectRequestedStartDate: 'Selecciona una fecha de inicio solicitada',
      from: 'Desde',
      selectFrequency: 'Selecciona la frecuencia con la que XMM debe administrar la membresía',
      frequency: 'frecuencia',
      hrs: 'hrs',
      preventAutomaticSync: '¿Prevenir sincronización automática si los cambios en la membresía exceden los límites de incremento y/o decremento?', 
      increase: 'Incremento',
      decrease: 'Decremento',
      step4title: 'Paso 4: Confirmación',
      step4description: '',
      objectId: 'ID del objeto',
      sourceParts: 'Partes de origen',
      sourcePart: 'Parte de origen',
      noThresholdSet: 'No se estableció un límite',
      savingSyncJob: 'Guardando...',
      group: 'Grupo',
      destinationPickerSuggestionsHeaderText: 'Destinos sugeridos',
      expandCollapse: 'Expandir/Contraer',
      sourceType: 'Tipo de origen',
      addSourcePart: 'Agregar parte de origen',
      excludeSourcePart: 'Excluir parte de origen',
      deleteLastSourcePartWarning: 'No se puede eliminar la última parte de origen.',
      errorOnSchema: 'Esperaba {0} pero recibió tipo {1} en {2}.',
    }
  },
  delete: 'Eliminar',
  edit: 'Editar',
  submit: 'Enviar',
  needHelp: '¿Necesitas ayuda?',
  next: 'Siguiente',
  cancel: 'Cancelar',
  close: 'Cerrar',
  learnMore: 'Aprende más',
  errorItemNotFound: 'Elemento no encontrado',
  welcome: 'Bienvenido',
  back: 'Regresar',
  backToDashboard: 'Regresar al dashboard',
  version: 'Versión',
  yes: 'Sí',
  no: 'No',
  privacyPolicy: 'Política de Privacidad',
};
