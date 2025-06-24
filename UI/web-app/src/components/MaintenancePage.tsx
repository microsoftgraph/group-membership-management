import { Text } from '@fluentui/react/lib/Text';
import { Icon } from '@fluentui/react/lib/Icon';
import { useStrings } from '../store/hooks';

export const MaintenancePage = () => {
  const strings = useStrings();
  return (
    <div style={{ padding: '20px', textAlign: 'center', display: 'flex', flexDirection: 'column', justifyContent: 'center', alignItems: 'center', minHeight: '100vh' }}>
      <Icon iconName="Settings" style={{ fontSize: '48px', color: '#605e5c', marginBottom: '16px' }} />
      <Text variant="xxLarge" style={{ display: 'block', marginBottom: '8px' }}>
        {strings.maintenanceTitle}
      </Text>
      <Text variant="medium" style={{ color: '#605e5c' }}>
        {strings.maintenanceMessage}
      </Text>
    </div>
  );
};