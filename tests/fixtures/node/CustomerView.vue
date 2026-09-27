<template>
  <div class="customer-view">
    <h1>{{ greeting }}</h1>
  </div>
</template>

<script lang="ts">
export default {
  data() {
    return {
      greeting: 'Hello Vue Customer!'
    };
  }
};
</script>

<style scoped>
.customer-view {
  font-family: sans-serif;
}
</style>
