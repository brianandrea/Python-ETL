﻿Vue.component("sm-datagrid-pager", {
    template: `
        <div class="dg-pager d-flex flex-nowrap align-items-center">
            <div class="dg-page-refresh-wrapper">
                <a href="#" class="dg-page dg-page-refresh btn btn-light btn-sm" @click.prevent="refresh">
                    <i class="fa fa-sync-alt" :class="{ 'fa-spin text-success': $parent.isBusy }"></i>
                </a>
            </div>
            
            <template v-if="totalPages > 1">
                <a href="#" class="dg-page dg-page-arrow btn btn-light btn-sm" @click.prevent="pageTo(1)" :class="{ disabled: !hasPrevPage }"><i class="fa fa-angle-double-left"></i></a>
                <a href="#" class="dg-page dg-page-arrow btn btn-light btn-sm" @click.prevent="pageTo(currentPageIndex - 1)" :class="{ disabled: !hasPrevPage }"><i class="fa fa-angle-left"></i></a>
            
                <a v-for="item in pageItems" href="#" @click.prevent="pageTo(item.page)" class="dg-page dg-page-number btn btn-light py-1 btn-sm d-none d-md-inline" :class="{ active: item.active }">
                    {{ item.label || item.page }}
                </a>
            
                <a href="#" class="dg-page dg-page-arrow btn btn-light btn-sm" @click.prevent="pageTo(currentPageIndex + 1)" :class="{ disabled: !hasNextPage }"><i class="fa fa-angle-right"></i></a>
                <a href="#" class="dg-page dg-page-arrow btn btn-light btn-sm" @click.prevent="pageTo(totalPages)" :class="{ disabled: !hasNextPage }"><i class="fa fa-angle-double-right"></i></a>
            </template>
            
            <div v-if="rows.length > 0" class="ml-auto d-flex align-items-center">
                <span class="dg-page text-muted text-truncate d-none d-md-inline pl-2">
                    <span class="d-none d-lg-inline">Anzeigen der Elemente </span>
                    <span>{{ firstItemIndex.toLocaleString() }}-{{ lastItemIndex.toLocaleString() }} von {{ total.toLocaleString() }}</span>
                </span>
                <div v-if="paging.showSizeChooser && paging.availableSizes?.length" class="dropdown d-flex align-items-center border-left pl-1 ml-3">
                    <a href="#" class="dg-page dg-page-size-chooser btn btn-light btn-sm dropdown-toggle text-truncate px-3" data-toggle="dropdown">
                        <span class="fwm">{{ command.pageSize }}</span> pro Seite
                    </a>
                    <div class="dropdown-menu">
                        <a v-for="size in paging.availableSizes" href="#" class="dropdown-item" @click.prevent="setPageSize(size)">{{ size }}</a>
                    </div>
                </div>
            </div>

            <div>
                <sm-datagrid-tools :options="options" :columns="columns" :paging="paging"></sm-datagrid-tools>
            </div>
        </div>
    `,

    props: {
        options: Object,
        paging: Object,
        command: Object,
        columns: Array,
        rows: Array,
        total: Number,
        maxPagesToDisplay: Number
    },

    computed: {
        currentPageIndex() {
            return this.command.page;
        },

        currentPageSize() {
            return this.command.pageSize;
        },

        totalPages() {
            return this.$parent.totalPages;
        },

        hasPrevPage() {
            return this.currentPageIndex > 1;
        },

        hasNextPage() {
            return this.currentPageIndex < this.totalPages;
        },

        isFirstPage() {
            return this.currentPageIndex <= 1;
        },

        isLastPage() {
            return this.currentPageIndex >= this.totalPages;
        },

        firstItemIndex() {
            return ((this.currentPageIndex - 1) * this.currentPageSize) + 1;
        },

        lastItemIndex() {
            return Math.min(this.total, (((this.currentPageIndex - 1) * this.currentPageSize) + this.currentPageSize));
        },

        pageItems() {
            var currentIndex = this.currentPageIndex;
            var totalPages = this.totalPages;
            var maxPages = this.maxPagesToDisplay;
            var start = 1;

            if (currentIndex > maxPages) {
                var v = currentIndex % maxPages;
                start = v === 0 ? currentIndex - maxPages + 1 : currentIndex - v + 1;
            }

            var p = start + maxPages - 1;
            p = Math.min(p, totalPages);

            var items = [];

            if (start > 1) {
                items.push({ page: start - 1, label: '...' });
            }

            for (var i = start; i <= p; i++) {
                items.push({ page: i, label: i.toString(), active: i === currentIndex });
            }

            if (p < totalPages) {
                items.push({ page: p + 1, label: '...' });
            }

            return items;
        }
    },

    methods: {
        refresh() {
            this.$parent.read();
        },

        pageTo(pageIndex) {
            if (pageIndex > 0 && pageIndex <= this.totalPages && !this.$parent.isBusy) {
                this.paging.pageIndex = pageIndex;
            }
        },

        setPageSize(size) {
            if (!this.$parent.isBusy) {
                if (size > this.command.pageSize) {
                    this.paging.pageIndex = 1;
                }
                this.paging.pageSize = size;
            }
        }
    }
});